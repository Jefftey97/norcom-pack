#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Updates;

public enum ActivationOutcome { Committed, Restored, CommittedNotificationUncertain }

public sealed class ActivationCoordinator
{
    private readonly Action<UpdateStore,LaunchAttempt,Process> _commit;
    public ActivationCoordinator():this((store,attempt,child)=>store.Commit(attempt.Id,attempt.Candidate.ExecutableHash,child.Id,attempt.StartedUtcTicks!.Value)) { }
    internal ActivationCoordinator(Action<UpdateStore,LaunchAttempt,Process> commit)=>_commit=commit;
    internal static string? ChildStateOverride(string launcherRoot,string? inherited,string localAppData)
    {
        var canonical=Path.Combine(localAppData,"NORCOM","Launcher");
        return string.IsNullOrWhiteSpace(inherited) && Path.GetFullPath(launcherRoot).Equals(Path.GetFullPath(canonical),StringComparison.OrdinalIgnoreCase)
            ?null:Path.GetDirectoryName(launcherRoot);
    }
    public async Task<ActivationOutcome> LaunchPendingAsync(UpdateStore store,TimeSpan startupBudget,CancellationToken ct)
    {
        var pending=store.Read().Pending??throw new InvalidOperationException("No prepared launcher activation.");
        if(pending.Phase!="prepared")throw new InvalidDataException("Interrupted activation needs recovery first.");
        var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var pipeName="norcom-launcher-"+pending.Id+"-"+Guid.NewGuid().ToString("N");
        using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(startupBudget);
        Process? child=null;
        bool committed=false;
        bool validatingImage=true;
        try
        {
            store.VerifyLocal(pending.Candidate);
            // Reject non-Windows/incorrect-architecture bytes before CreateProcess:
            // Windows may show a blocking compatibility dialog for a malformed EXE.
            using(var executable=File.OpenRead(store.PayloadPath(pending.Candidate)))
            using(var pe=new PEReader(executable))
            {
                var headers=pe.PEHeaders;
                if(headers.CoffHeader.Machine!=Machine.Amd64 || headers.PEHeader?.Magic!=PEMagic.PE32Plus ||
                    headers.PEHeader.AddressOfEntryPoint==0 || (headers.CoffHeader.Characteristics & Characteristics.Dll)!=0)
                    throw new InvalidDataException("The signed launcher payload is not a Windows x64 executable.");
            }
            validatingImage=false;
            var info=new ProcessStartInfo(store.PayloadPath(pending.Candidate)){UseShellExecute=false,WorkingDirectory=store.Root,CreateNoWindow=true};
            info.ArgumentList.Add("--norcom-payload");info.ArgumentList.Add(pending.Id);info.ArgumentList.Add(pipeName);info.ArgumentList.Add(nonce);
            var stateOverride=ChildStateOverride(store.Root,Environment.GetEnvironmentVariable("NORCOM_STATE_DIR"),NorcomPaths.LocalAppData);
            if(stateOverride!=null)info.Environment["NORCOM_STATE_DIR"]=stateOverride;
            child=Process.Start(info)??throw new IOException("Launcher process was not created.");
            var started=child.StartTime.ToUniversalTime().Ticks;
            pending=store.RecordLaunched(pending.Id,child.Id,started);
            await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            var hello=await ActivationWire.ReadAsync(pipe,deadline.Token).ConfigureAwait(false);
            Validate(hello,"hello",pending,nonce,child,store);
            await ActivationWire.WriteAsync(pipe,hello with{Stage="authorized"},deadline.Token).ConfigureAwait(false);
            var ready=await ActivationWire.ReadAsync(pipe,deadline.Token).ConfigureAwait(false);
            Validate(ready,"ready",pending,nonce,child,store);
            _commit(store,pending,child);
            committed=true;
            await ActivationWire.WriteAsync(pipe,ready with{Stage="committed"},deadline.Token).ConfigureAwait(false);
            return ActivationOutcome.Committed;
        }
        catch(Exception failure)
        {
            // The child may already have received the final acknowledgement and enabled
            // actions. After durable commit, never kill it or start a fallback beside it.
            if(committed)return ActivationOutcome.CommittedNotificationUncertain;
            UpdateState observed;
            try {observed=store.Read();}
            catch(Exception stateError){throw new IOException("The launcher update outcome could not be verified. Recovery files were kept.",stateError);}
            if(observed.Pending==null && observed.Outcome=="healthy" && observed.Active==pending.Candidate && observed.Previous==pending.Previous)
            {
                // A durable write can succeed even when its caller reports an error.
                // Prove that transition before deciding to stop or restore anything.
                if(child!=null)
                {
                    try
                    {
                        using var acknowledge=new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await ActivationWire.WriteAsync(pipe,new ActivationMessage("committed",pending.Id,nonce,pending.Candidate.ExecutableHash,child.Id,pending.StartedUtcTicks!.Value),acknowledge.Token).ConfigureAwait(false);
                    }
                    catch { }
                }
                return ActivationOutcome.CommittedNotificationUncertain;
            }
            if(observed.Pending?.Id!=pending.Id)throw new IOException("The launcher activation outcome is uncertain; no restoration was reported.",failure);
            // This Process object retains the exact handle created here; never kill by name
            // or a re-resolved PID. If it cannot stop, do not claim rollback completed.
            if(child!=null && !child.HasExited)
            {
                child.Kill(entireProcessTree:false);
                if(!child.WaitForExit(10000))throw new IOException("The candidate did not stop; recovery is pending.");
            }
            var reason=validatingImage && (failure is BadImageFormatException || failure is InvalidDataException || failure is CryptographicException)
                ?RollbackReason.InvalidPayload:RollbackReason.CandidateFailed;
            store.Rollback(pending.Id,reason);
            return ActivationOutcome.Restored;
        }
        finally {child?.Dispose();}
    }
    private static void Validate(ActivationMessage message,string stage,LaunchAttempt pending,string nonce,Process child,UpdateStore store)
    {
        if(message.Stage!=stage || message.Attempt!=pending.Id || message.Nonce!=nonce ||
            message.ExecutableHash!=pending.Candidate.ExecutableHash || message.ProcessId!=child.Id ||
            message.StartedUtcTicks!=pending.StartedUtcTicks || child.HasExited ||
            child.StartTime.ToUniversalTime().Ticks!=pending.StartedUtcTicks ||
            !string.Equals(Path.GetFullPath(child.MainModule?.FileName??""),store.PayloadPath(pending.Candidate),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Launcher health acknowledgement did not match the exact candidate.");
    }
    public void RecoverInterrupted(UpdateStore store)
    {
        var pending=store.Read().Pending;
        if(pending==null)return;
        if(pending.Phase=="launched" && pending.ProcessId is int pid)
        {
            Process? process=null;
            try {process=Process.GetProcessById(pid);}catch(ArgumentException){}
            using(process)
            {
                if(process!=null && !process.HasExited && process.StartTime.ToUniversalTime().Ticks==pending.StartedUtcTicks)
                {
                    if(!string.Equals(Path.GetFullPath(process.MainModule?.FileName??""),store.PayloadPath(pending.Candidate),StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Pending launcher process identity could not be confirmed.");
                    process.Kill(entireProcessTree:false);
                    if(!process.WaitForExit(10000))throw new IOException("Interrupted launcher did not stop.");
                }
            }
        }
        // Prepared but unrecorded children cannot receive authorization, and their
        // bounded pipe handshake exits before loading WPF or enabling any action.
        store.Rollback(pending.Id,pending.Phase=="prepared"?RollbackReason.InterruptedBeforeLaunch:RollbackReason.CandidateFailed);
    }
}
