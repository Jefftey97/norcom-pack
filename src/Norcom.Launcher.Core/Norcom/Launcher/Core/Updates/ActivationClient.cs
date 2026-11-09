#nullable enable
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Updates;

public sealed record ActivationMessage(string Stage,string Attempt,string Nonce,string ExecutableHash,int ProcessId,long StartedUtcTicks);

internal static class ActivationWire
{
    public static async Task WriteAsync(Stream stream,ActivationMessage message,CancellationToken ct)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(message);
        var length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);
        await stream.WriteAsync(length,ct).ConfigureAwait(false);await stream.WriteAsync(bytes,ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    public static async Task<ActivationMessage> ReadAsync(Stream stream,CancellationToken ct)
    {
        var header=new byte[4];await stream.ReadExactlyAsync(header,ct).ConfigureAwait(false);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length<1 || length>2048)throw new InvalidDataException("Invalid launcher acknowledgement size.");
        var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,ct).ConfigureAwait(false);
        using var doc=JsonDocument.Parse(bytes);
        ReleaseVerifier.ExactProperties(doc.RootElement,"Stage","Attempt","Nonce","ExecutableHash","ProcessId","StartedUtcTicks");
        return doc.RootElement.Deserialize<ActivationMessage>()??throw new InvalidDataException("Empty launcher acknowledgement.");
    }
}

public sealed class ActivationClient:IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly ActivationMessage _identity;
    private readonly TaskCompletionSource<bool> _committed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ActivationClient(NamedPipeClientStream pipe,ActivationMessage identity)
    {
        (_pipe,_identity)=(pipe,identity);
        _=MonitorCommitAsync();
    }
    private async Task MonitorCommitAsync()
    {
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            if(await ActivationWire.ReadAsync(_pipe,deadline.Token).ConfigureAwait(false)!=_identity with{Stage="committed"})
                throw new InvalidDataException("Launcher activation was not committed.");
            _committed.TrySetResult(true);
        }
        catch(Exception ex)
        {
            _committed.TrySetException(ex);
            // This is an authenticated candidate process. The watchdog is independent of
            // its UI thread: supervisor death must not leave a disabled window holding
            // the single-instance mutex indefinitely, even if WPF never renders.
            Environment.Exit(1);
        }
    }
    public static async Task<ActivationClient> AuthorizeAsync(UpdateStore store,string attempt,string pipeName,string nonce,CancellationToken ct)
    {
        if(!Guid.TryParseExact(attempt,"N",out _) || !ReleaseVerifier.IsHash(nonce) ||
            !pipeName.StartsWith("norcom-launcher-"+attempt+"-",StringComparison.Ordinal) ||
            !Guid.TryParseExact(pipeName.Substring(("norcom-launcher-"+attempt+"-").Length),"N",out _))
            throw new InvalidDataException("Invalid launcher activation arguments.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        LaunchAttempt? pending;
        do
        {
            pending=store.Read().Pending;
            if(pending?.Id!=attempt)throw new InvalidDataException("This launcher attempt is no longer pending.");
            if(pending.Phase=="launched")break;
            await Task.Delay(30,deadline.Token).ConfigureAwait(false);
        }while(true);
        using var self=Process.GetCurrentProcess();
        var ticks=self.StartTime.ToUniversalTime().Ticks;
        if(pending.ProcessId!=Environment.ProcessId || pending.StartedUtcTicks!=ticks ||
            !string.Equals(Path.GetFullPath(Environment.ProcessPath??""),store.PayloadPath(pending.Candidate),StringComparison.OrdinalIgnoreCase) ||
            UpdateStore.FileHash(Environment.ProcessPath!)!=pending.Candidate.ExecutableHash)
            throw new InvalidDataException("This process is not the selected launcher candidate.");
        var identity=new ActivationMessage("hello",attempt,nonce,pending.Candidate.ExecutableHash,Environment.ProcessId,ticks);
        var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await ActivationWire.WriteAsync(pipe,identity,deadline.Token).ConfigureAwait(false);
            if(await ActivationWire.ReadAsync(pipe,deadline.Token).ConfigureAwait(false)!=identity with{Stage="authorized"})
                throw new InvalidDataException("Launcher startup was not authorized.");
            return new ActivationClient(pipe,identity);
        }
        catch {pipe.Dispose();throw;}
    }
    public async Task ReadyAsync(CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await ActivationWire.WriteAsync(_pipe,_identity with{Stage="ready"},deadline.Token).ConfigureAwait(false);
        await _committed.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
    }
    public void Dispose()=>_pipe.Dispose();
}
