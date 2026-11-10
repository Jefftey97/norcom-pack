#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Updates;

public sealed record PayloadIdentity(string PackageHash, string PayloadDigest, string Version, string ExecutableHash);
public sealed record DeferredPayload(PayloadIdentity Candidate,bool PreviouslyHealthy);
public enum RollbackReason { CandidateFailed, InterruptedBeforeLaunch, InvalidPayload }
public sealed record LaunchAttempt(string Id, PayloadIdentity Candidate, PayloadIdentity? Previous, string Mode,
    string Phase, int? ProcessId = null, long? StartedUtcTicks = null,int? CoordinatorPid=null,long? CoordinatorStartedUtcTicks=null,bool Recovery=false);
public sealed record UpdateState(int Schema, string InstallId, string BootstrapHash, long Sequence,
    string? AcceptedDigest, PayloadIdentity? Active, PayloadIdentity? Previous, LaunchAttempt? Pending,
    string[] Quarantined, string Outcome,DeferredPayload? DeferredRetry=null);

public sealed class UpdateStore
{
    private readonly ReleaseVerifier _verifier;
    private FileStream? _lock;
    public string Root { get; }
    public string BootstrapPath => Path.Combine(Root,"NorcomLauncher.exe");
    private string UpdateDirectory => Path.Combine(Root,"update");
    private string StatePath => Path.Combine(UpdateDirectory,"state.json");
    public UpdateStore(string root, ReleaseVerifier verifier)
    {
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _verifier=verifier;
        OwnedFiles.CheckAncestors(Root);
    }
    public IDisposable Acquire()
    {
        if(_lock!=null) throw new IOException("This updater already owns the installation lock.");
        OwnedFiles.CheckAncestors(UpdateDirectory);
        Directory.CreateDirectory(UpdateDirectory);
        var path=Path.Combine(UpdateDirectory,"coordinator.lock");
        OwnedFiles.CheckAncestors(path);
        _lock=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        return new Lease(this);
    }
    public UpdateState Read()
    {
        if(!File.Exists(StatePath))
        {
            Guard();
            if(Directory.Exists(Path.Combine(Root,"app")) ||
                Directory.EnumerateFileSystemEntries(UpdateDirectory).Any(x=>Path.GetFileName(x)!="coordinator.lock"))
                throw new InvalidDataException("Existing launcher recovery data has no channel watermark. Files were kept for recovery.");
            var initial=new UpdateState(1,Guid.NewGuid().ToString("N"),FileHash(BootstrapPath),0,null,null,null,null,Array.Empty<string>(),"ready");
            Save(initial);
            return initial;
        }
        var state=OwnedFiles.Read<UpdateState>(StatePath);
        if(state.Schema!=1 || !Guid.TryParseExact(state.InstallId,"N",out _) || !ReleaseVerifier.IsHash(state.BootstrapHash) ||
            state.Sequence<0 || (state.Sequence==0 ? state.AcceptedDigest!=null : !ReleaseVerifier.IsHash(state.AcceptedDigest!)) ||
            state.Quarantined==null || state.Quarantined.Length>256 || state.Quarantined.Any(x=>!ReleaseVerifier.IsHash(x)))
            throw new InvalidDataException("Launcher recovery state is invalid; original files were kept.");
        if(state.Active!=null) ValidateIdentity(state.Active);
        if(state.Previous!=null) ValidateIdentity(state.Previous);
        if(state.DeferredRetry!=null)ValidateIdentity(state.DeferredRetry.Candidate);
        if((state.Sequence==0 && (state.Active!=null || state.Previous!=null || state.Pending!=null || state.DeferredRetry!=null || state.Quarantined.Length!=0 || Directory.Exists(Path.Combine(Root,"app")))) ||
            (state.Active!=null && (state.Active.PackageHash==state.Previous?.PackageHash || state.Quarantined.Contains(state.Active.PackageHash))) ||
            (state.Previous!=null && state.Quarantined.Contains(state.Previous.PackageHash)) ||
            (state.DeferredRetry!=null && (state.DeferredRetry.Candidate.PackageHash==state.Active?.PackageHash || state.Quarantined.Contains(state.DeferredRetry.Candidate.PackageHash))) ||
            !new[]{"ready","starting","healthy","restored"}.Contains(state.Outcome) ||
            ((state.Pending!=null)!=(state.Outcome=="starting")))
            throw new InvalidDataException("Launcher recovery state is inconsistent.");
        if(state.Pending is { } pending)
        {
            ValidateIdentity(pending.Candidate);
            if(pending.Previous!=null)ValidateIdentity(pending.Previous);
            if(!Guid.TryParseExact(pending.Id,"N",out _) || (pending.Mode!="update" && pending.Mode!="open" && pending.Mode!="retry") ||
                (pending.Phase!="prepared" && pending.Phase!="launched") ||
                (pending.Phase=="launched" && (pending.ProcessId is not >0 || pending.StartedUtcTicks is not >0)) ||
                (pending.Phase=="prepared" && (pending.ProcessId!=null || pending.StartedUtcTicks!=null)) ||
                ((pending.CoordinatorPid==null)!=(pending.CoordinatorStartedUtcTicks==null)) ||
                (pending.CoordinatorPid!=null && (pending.CoordinatorPid<=0 || pending.CoordinatorStartedUtcTicks<=0)) ||
                (pending.Mode!="open" && (pending.Previous!=state.Active || pending.Candidate.PackageHash==state.Active?.PackageHash)) ||
                (pending.Mode=="retry" && (state.DeferredRetry?.PreviouslyHealthy!=true || pending.Candidate.PackageHash!=state.DeferredRetry.Candidate.PackageHash)) ||
                (pending.Mode=="open" && (pending.Candidate!=state.Active || pending.Previous!=state.Previous)) ||
                state.Quarantined.Contains(pending.Candidate.PackageHash))
                throw new InvalidDataException("Launcher launch receipt is invalid.");
        }
        return state;
    }
    public void Accept(VerifiedRelease release)
    {
        Guard();
        // A recovery verification object cannot authorize a new installation.
        var fresh=_verifier.Verify(release.Envelope,DateTimeOffset.UtcNow);
        var state=Read();
        if(state.Pending!=null)throw new InvalidDataException("Another launcher activation is pending.");
        if(fresh.Payload.Sequence<state.Sequence ||
            (fresh.Payload.Sequence==state.Sequence && fresh.PayloadDigest!=state.AcceptedDigest))
            throw new InvalidDataException("Launcher release is older than the accepted channel or reuses a sequence.");
        if(state.Quarantined.Contains(fresh.Payload.PackageSha256,StringComparer.Ordinal))
            throw new InvalidDataException("This launcher package previously failed to start.");
        Save(state with{Sequence=fresh.Payload.Sequence,AcceptedDigest=fresh.PayloadDigest});
    }
    public PayloadIdentity Stage(VerifiedRelease release,string package)
    {
        Guard();Accept(release);
        var p=release.Payload;
        var identity=new PayloadIdentity(p.PackageSha256,release.PayloadDigest,p.Version,p.ExecutableSha256);
        var destination=Path.GetDirectoryName(PayloadPath(identity))!;
        if(Directory.Exists(destination))
        {
            ReleasePackage.ValidateArchive(release,package);
            var exe=PayloadPath(identity);
            if(new FileInfo(exe).Length!=p.ExecutableBytes || FileHash(exe)!=p.ExecutableSha256)
                throw new InvalidDataException("Stored package bytes differ from the renewed signed release.");
            var receipt=Path.Combine(destination,"release-"+identity.PayloadDigest+".json");
            OwnedFiles.CheckAncestors(receipt);
            if(!File.Exists(receipt))OwnedFiles.WriteNew(receipt,Encoding.UTF8.GetBytes(release.Envelope));
            VerifyLocal(identity);return identity;
        }
        var stage=Path.Combine(UpdateDirectory,"stage-"+Guid.NewGuid().ToString("N"));
        try
        {
            ReleasePackage.Extract(release,package,stage);
            OwnedFiles.WriteNew(Path.Combine(stage,"release-"+identity.PayloadDigest+".json"),Encoding.UTF8.GetBytes(release.Envelope));
            OwnedFiles.CheckAncestors(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(stage,destination);
        }
        finally
        {
            // The uniquely named staging folder was created by this call and never
            // holds an accepted payload; installed and rollback folders are untouched.
            DiscardStage(stage);
        }
        VerifyLocal(identity);
        return identity;
    }
    private void DiscardStage(string stage)
    {
        try
        {
            if(!Directory.Exists(stage))return;
            OwnedFiles.CheckAncestors(stage);
            if(!string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)),Path.GetFullPath(UpdateDirectory),StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(stage).StartsWith("stage-",StringComparison.Ordinal) ||
                (File.GetAttributes(stage) & FileAttributes.ReparsePoint)!=0)return;
            Directory.Delete(stage,recursive:true);
        }
        catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) { }
    }
    public string PayloadPath(PayloadIdentity identity)
    {
        ValidateIdentity(identity);
        var path=Path.Combine(Root,"app",identity.PackageHash,"NorcomLauncher.exe");
        OwnedFiles.CheckAncestors(path);
        return path;
    }
    public LaunchAttempt Prepare(PayloadIdentity candidate,bool reopening=false)
    {
        Guard();var state=Read();
        if(state.Pending!=null || state.Quarantined.Contains(candidate.PackageHash,StringComparer.Ordinal))
            throw new InvalidDataException("Launcher activation is pending or this package is quarantined.");
        var verified=VerifyLocal(candidate);
        if(reopening)
        {
            if(candidate!=state.Active)throw new InvalidDataException("Only the committed payload can be reopened.");
        }
        else
        {
            // New installation must still be fresh and be the exact latest accepted release.
            _verifier.Verify(verified.Envelope,DateTimeOffset.UtcNow);
            if(verified.Payload.Sequence!=state.Sequence || verified.PayloadDigest!=state.AcceptedDigest ||
                VersionCompare.Compare(candidate.Version,state.Active?.Version??"2.0.45")<=0)
                throw new InvalidDataException("Candidate is not the accepted newer launcher release.");
        }
        var previous=reopening?state.Previous:state.Active;
        if(reopening)
        {
            previous=SelectVerifiedFallback(state,previous);
            state=state with{Previous=previous};
        }
        else VerifyPrevious(state,previous);
        var mode=reopening?"open":state.DeferredRetry is {PreviouslyHealthy:true} retry && retry.Candidate.PackageHash==candidate.PackageHash?"retry":"update";
        var attempt=new LaunchAttempt(Guid.NewGuid().ToString("N"),candidate,previous,mode,"prepared",Recovery:reopening && state.Outcome=="restored");
        Save(state with{Pending=attempt,Outcome="starting"});
        return attempt;
    }
    public LaunchAttempt RecordLaunched(string attempt,int pid,long startedTicks)
    {
        Guard();var state=Read();var pending=Pending(state,attempt);
        if(pending.Phase!="prepared" || pid<=0 || startedTicks<=0)throw new InvalidDataException("Unexpected launcher process receipt.");
        pending=pending with{Phase="launched",ProcessId=pid,StartedUtcTicks=startedTicks};
        Save(state with{Pending=pending});return pending;
    }
    public void RecordCoordinator(string attempt,int pid,long ticks)
    {
        Guard();var state=Read();var pending=Pending(state,attempt);
        if(pending.Phase!="prepared" || pid<=0 || ticks<=0)throw new InvalidDataException("Invalid coordinator receipt.");
        Save(state with{Pending=pending with{CoordinatorPid=pid,CoordinatorStartedUtcTicks=ticks}});
    }
    public void Commit(string attempt,string exeHash,int pid,long startedTicks)
    {
        Guard();var state=Read();var pending=Pending(state,attempt);
        if(pending.Phase!="launched" || pending.Candidate.ExecutableHash!=exeHash ||
            pending.ProcessId!=pid || pending.StartedUtcTicks!=startedTicks)
            throw new InvalidDataException("Launcher health acknowledgement does not match its launch.");
        VerifyLocal(pending.Candidate);
        Save(state with{Active=pending.Candidate,Previous=pending.Previous,Pending=null,Outcome="healthy",DeferredRetry=pending.Mode=="open"?state.DeferredRetry:null});
    }
    public void Rollback(string attempt,RollbackReason reason=RollbackReason.CandidateFailed)
    {
        Guard();var state=Read();var pending=Pending(state,attempt);
        var previous=SelectVerifiedFallback(state,pending.Previous);
        var invalid=reason==RollbackReason.InvalidPayload;
        try {VerifyLocal(pending.Candidate);}
        catch(Exception ex) when(ex is InvalidDataException || ex is CryptographicException || ex is System.Text.Json.JsonException || ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {invalid=true;}
        var quarantineCandidate=invalid || (reason==RollbackReason.CandidateFailed && pending.Mode=="update");
        var quarantine=quarantineCandidate?state.Quarantined.Append(pending.Candidate.PackageHash).Distinct(StringComparer.Ordinal).ToArray():state.Quarantined;
        if(quarantine.Length>256)throw new InvalidDataException("Launcher recovery history needs owner attention.");
        // Executables are immutable. Switching the pointer restores the exact verified
        // prior payload; no auth, settings, saves or game files are copied or restored.
        var deferred=quarantineCandidate?null:new DeferredPayload(pending.Candidate,pending.Mode is "open" or "retry");
        Save(state with{Active=previous,Previous=null,Pending=null,Quarantined=quarantine,Outcome="restored",DeferredRetry=deferred});
    }
    public void RecoverCommitted()
    {
        Guard();var state=Read();
        if(state.Pending!=null || state.Active==null)throw new InvalidDataException("No committed launcher can be recovered.");
        var previous=SelectVerifiedFallback(state,state.Previous);
        var quarantine=state.Quarantined.Append(state.Active.PackageHash).Distinct(StringComparer.Ordinal).ToArray();
        if(quarantine.Length>256)throw new InvalidDataException("Launcher recovery history needs owner attention.");
        Save(state with{Active=previous,Previous=null,Quarantined=quarantine,Outcome="restored",DeferredRetry=state.DeferredRetry?.Candidate.PackageHash==previous?.PackageHash?null:state.DeferredRetry});
    }
    public void AcknowledgeBootstrapRecovery()
    {
        Guard();var state=Read();
        if(state.Pending!=null || state.Active!=null || state.Outcome!="restored")throw new InvalidOperationException("No bootstrap recovery notice is pending.");
        Save(state with{Outcome="ready"});
    }
    private PayloadIdentity? SelectVerifiedFallback(UpdateState state,PayloadIdentity? previous)
    {
        try {VerifyPrevious(state,previous);return previous;}
        catch(Exception ex) when(previous!=null && (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is CryptographicException || ex is UnauthorizedAccessException))
        {
            // A damaged older payload must not strand a healthy current version.
            // Keep its files, and select the original bootstrap only after hashing it.
            VerifyPrevious(state,null);return null;
        }
    }
    internal VerifiedRelease VerifyLocal(PayloadIdentity identity)
    {
        var exe=PayloadPath(identity);
        var directory=Path.GetDirectoryName(exe)!;
        var envelope=Path.Combine(directory,"release-"+identity.PayloadDigest+".json");
        if(!File.Exists(envelope))envelope=Path.Combine(directory,"release-envelope.json");
        OwnedFiles.CheckAncestors(envelope);
        if(!File.Exists(envelope))throw new InvalidDataException("No signed receipt matches this stored launcher identity.");
        if(new FileInfo(envelope).Length>ReleaseVerifier.MaximumEnvelopeBytes)throw new InvalidDataException("Stored release envelope exceeds its limit.");
        var verified=_verifier.Verify(File.ReadAllText(envelope,Encoding.UTF8),DateTimeOffset.UtcNow,installing:false);
        if(verified.PayloadDigest!=identity.PayloadDigest || verified.Payload.PackageSha256!=identity.PackageHash ||
            verified.Payload.ExecutableSha256!=identity.ExecutableHash || verified.Payload.Version!=identity.Version ||
            new FileInfo(exe).Length!=verified.Payload.ExecutableBytes || FileHash(exe)!=identity.ExecutableHash)
            throw new InvalidDataException("Stored launcher differs from its accepted release.");
        return verified;
    }
    private void VerifyPrevious(UpdateState state,PayloadIdentity? previous)
    {
        if(previous!=null)VerifyLocal(previous);
        else if(FileHash(BootstrapPath)!=state.BootstrapHash)throw new InvalidDataException("The recovery launcher changed; automatic recovery was not confirmed.");
    }
    private static LaunchAttempt Pending(UpdateState state,string id)
        => state.Pending is {} pending && pending.Id==id?pending:throw new InvalidDataException("Launcher attempt is no longer pending.");
    private static void ValidateIdentity(PayloadIdentity identity)
    {
        if(identity==null || !ReleaseVerifier.IsHash(identity.PackageHash) || !ReleaseVerifier.IsHash(identity.PayloadDigest) ||
            !ReleaseVerifier.IsHash(identity.ExecutableHash) || !ReleaseVerifier.CanonicalVersion(identity.Version))
            throw new InvalidDataException("Invalid launcher payload identity.");
    }
    internal static string FileHash(string path)
    {
        OwnedFiles.CheckAncestors(path);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        return ReleasePackage.Hex(SHA256.HashData(stream));
    }
    private void Guard(){if(_lock==null || _lock.SafeFileHandle.IsClosed)throw new InvalidOperationException("Launcher update state requires its installation lock.");}
    private void Save(UpdateState state){Guard();OwnedFiles.WriteAtomic(StatePath,state);}
    private sealed class Lease:IDisposable
    {
        private UpdateStore? _owner;
        public Lease(UpdateStore owner)=>_owner=owner;
        public void Dispose(){var owner=_owner;_owner=null;if(owner!=null){owner._lock?.Dispose();owner._lock=null;}}
    }
}
