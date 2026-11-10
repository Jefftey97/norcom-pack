#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Core.Updates;

namespace Norcom.Launcher.Services;

public static class BridgeRuntime
{
    private static readonly TaskCompletionSource<bool> Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ActivationClient? _activation;
    private static bool _acknowledgeBootstrapRecovery;
    public static string BootstrapPath=>Path.Combine(NorcomPaths.LauncherDir,"NorcomLauncher.exe");
    public static string RecoveryMessage {get;private set;}="";
    public static bool HandoffScheduled {get;private set;}
    public static Task<bool> WaitForReadyAsync()=>Ready.Task;
    public static async Task MarkShellReadyAsync()
    {
        try
        {
            if(_activation!=null)await _activation.ReadyAsync(CancellationToken.None).ConfigureAwait(true);
            else if(_acknowledgeBootstrapRecovery)
            {
                var store=new UpdateStore(NorcomPaths.LauncherDir,ReleaseTrust.CreateVerifier());
                using var lease=store.Acquire();store.AcknowledgeBootstrapRecovery();_acknowledgeBootstrapRecovery=false;
            }
            Ready.TrySetResult(true);
        }
        catch {Ready.TrySetResult(false);throw;}
        finally {_activation?.Dispose();_activation=null;}
    }
    public static async Task<bool> EnterAsync(string[] args)
    {
        // Game preparation may run on background threads. It must never tear down
        // the UI mid-operation or fall back to the old unsigned updater. Bridge
        // updates run only at idle startup or from the explicit UI command.
        BridgeUpdateRouting.Schedule=_=>Task.FromResult(false);
        var store=new UpdateStore(NorcomPaths.LauncherDir,ReleaseTrust.CreateVerifier());
        var current=Path.GetFullPath(Environment.ProcessPath??throw new IOException("Launcher executable path is unavailable."));
        if(args.Length==4 && args[0]=="--norcom-payload")
        {
            _activation=await ActivationClient.AuthorizeAsync(store,args[1],args[2],args[3],CancellationToken.None).ConfigureAwait(false);
            if(store.Read().Pending?.Recovery==true)RecoveryMessage="The previous launcher was restored after an update could not start. Your settings and game files were kept.";
            return true;
        }
        if(!current.Equals(store.BootstrapPath,StringComparison.OrdinalIgnoreCase))
        {
            // Opening an immutable payload or the old alternate shortcut always goes
            // through the stable entry point, never through legacy promotion scripts.
            if(!File.Exists(store.BootstrapPath))throw new IOException("Open the installed NORCOM Launcher shortcut.");
            var underPayload=current.StartsWith(Path.Combine(store.Root,"app")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
            var alternate=LauncherInstall.AltExeFor(store.BootstrapPath);
            if(!underPayload && !current.Equals(alternate,StringComparison.OrdinalIgnoreCase))throw new IOException("Open the installed NORCOM Launcher shortcut.");
            Process.Start(new ProcessStartInfo(store.BootstrapPath){UseShellExecute=false,WorkingDirectory=store.Root});
            return false;
        }
        var worker=args.Length==4 && args[0]=="--norcom-activate";
        if(args.Length!=0 && !worker)throw new InvalidDataException("Unsupported launcher activation request.");
        using var lease=await AcquireForStartupAsync(store,worker).ConfigureAwait(false);
        var coordinator=new ActivationCoordinator();
        var state=store.Read();
        if(worker)
        {
            if(state.Pending?.Id!=args[1] || state.Pending.Phase!="prepared" ||
                !int.TryParse(args[2],out var parentId) || !long.TryParse(args[3],out var parentTicks))
                throw new InvalidDataException("Launcher update request is no longer pending.");
            await WaitForPreviousUiAsync(parentId,parentTicks).ConfigureAwait(false);
            var outcome=await coordinator.LaunchPendingAsync(store,TimeSpan.FromMinutes(3),CancellationToken.None).ConfigureAwait(false);
            if(outcome!=ActivationOutcome.Restored)return false;
        }
        else
        {
            if(state.Pending is {Phase:"prepared",CoordinatorPid:{} coordinatorPid,CoordinatorStartedUtcTicks:{} coordinatorTicks})
            {
                try
                {
                    using var process=Process.GetProcessById(coordinatorPid);
                    if(!process.HasExited && process.StartTime.ToUniversalTime().Ticks==coordinatorTicks &&
                        string.Equals(process.MainModule?.FileName,store.BootstrapPath,StringComparison.OrdinalIgnoreCase))return false;
                }
                catch(ArgumentException) { }
            }
            coordinator.RecoverInterrupted(store);
            if(SingleInstance.ActivateExisting())return false;
        }
        for(var attempt=0;attempt<3;attempt++)
        {
            state=store.Read();
            if(state.Outcome=="restored")RecoveryMessage="The previous launcher was restored after an update could not start. Your settings and game files were kept.";
            if(state.Active==null){_acknowledgeBootstrapRecovery=state.Outcome=="restored";return true;}
            try {store.Prepare(state.Active,reopening:true);}
            catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException || ex is System.Security.Cryptography.CryptographicException)
            {
                store.RecoverCommitted();continue;
            }
            var outcome=await coordinator.LaunchPendingAsync(store,TimeSpan.FromMinutes(3),CancellationToken.None).ConfigureAwait(false);
            if(outcome!=ActivationOutcome.Restored)return false;
        }
        throw new IOException("Launcher recovery needs attention. Recovery files have been kept.");
    }
    private static async Task WaitForPreviousUiAsync(int pid,long ticks)
    {
        if(pid<=0 || ticks<=0 || pid==Environment.ProcessId)throw new InvalidDataException("Invalid previous launcher identity.");
        Process? previous=null;
        try{previous=Process.GetProcessById(pid);}catch(ArgumentException){}
        using(previous)
        {
            if(previous==null || previous.HasExited || previous.StartTime.ToUniversalTime().Ticks!=ticks)return;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await previous.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
    }
    private static async Task<IDisposable> AcquireForStartupAsync(UpdateStore store,bool worker)
    {
        var until=DateTime.UtcNow.AddSeconds(worker?30:3);
        while(true)
        {
            try{return store.Acquire();}
            catch(IOException ex) when((ex.HResult & 0xffff)==32 && DateTime.UtcNow<until)
            {await Task.Delay(100).ConfigureAwait(false);}
        }
    }
    public static async Task<bool> CheckAndScheduleAsync(Action<string,double>? report,CancellationToken ct,bool allowRetry=false)
    {
        if(QaGates.OfflineCheckRequested()){report?.Invoke("Offline verification mode: automatic update checks are paused.",1);return false;}
        if(!Ready.Task.IsCompletedSuccessfully || !Ready.Task.Result)return false;
        if(GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))
        {
            report?.Invoke("Close Minecraft before updating the launcher.",1);return false;
        }
        var verifier=ReleaseTrust.CreateVerifier();
        var store=new UpdateStore(NorcomPaths.LauncherDir,verifier);
        using var lease=await AcquireForStartupAsync(store,false).ConfigureAwait(true);
        if(store.Read().Pending!=null)throw new IOException("A launcher update is already pending.");
        using var channel=new SignedChannelClient(verifier);
        report?.Invoke("Checking signed launcher updates",0.05);
        var release=await channel.FetchAsync(ct).ConfigureAwait(true);
        if(release==null){report?.Invoke("No signed launcher release is published yet.",1);return false;}
        store.Accept(release);
        if(!allowRetry && store.Read().DeferredRetry?.Candidate.PackageHash==release.Payload.PackageSha256)
        {
            report?.Invoke("The last launcher attempt was interrupted. Use Check for updates when you want to retry.",1);return false;
        }
        if(VersionCompare.Compare(release.Payload.Version,NorcomVersion.Current)<=0)
        {report?.Invoke("Launcher "+NorcomVersion.Current+" is up to date.",1);return false;}
        var package=Path.Combine(store.Root,"update","download-"+Guid.NewGuid().ToString("N")+".zip");
        report?.Invoke("Downloading verified launcher "+release.Payload.Version,0.15);
        PayloadIdentity identity;
        try
        {
            await channel.DownloadAsync(release,package,ct).ConfigureAwait(true);
            identity=store.Stage(release,package);
        }
        finally
        {
            // A failed or cancelled download/stage must not leave a ~70 MB archive behind.
            try {if(File.Exists(package))File.Delete(package);}
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {LauncherLog.Write("launcher update package kept "+ex.GetType().Name);}
        }
        var attempt=store.Prepare(identity);
        using var current=Process.GetCurrentProcess();
        var worker=new ProcessStartInfo(store.BootstrapPath){UseShellExecute=false,WorkingDirectory=store.Root,CreateNoWindow=true};
        worker.ArgumentList.Add("--norcom-activate");worker.ArgumentList.Add(attempt.Id);
        worker.ArgumentList.Add(current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        worker.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            using var started=Process.Start(worker)??throw new IOException("Could not start the independent launcher updater.");
            store.RecordCoordinator(attempt.Id,started.Id,started.StartTime.ToUniversalTime().Ticks);
            HandoffScheduled=true;
        }
        catch {store.Rollback(attempt.Id,RollbackReason.InterruptedBeforeLaunch);throw;}
        report?.Invoke("Restarting into the verified launcher",1);
        return true;
    }
}
