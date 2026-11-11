using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class UpdateStoreTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"norcom-update-store-"+Guid.NewGuid().ToString("N"));
    private readonly ECDsa key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] bootstrap=Encoding.UTF8.GetBytes("bootstrap; never execute");
    private ReleaseVerifier Verifier=>new(new Dictionary<string,string>{{"test-only",key.ExportSubjectPublicKeyInfoPem()}});
    public UpdateStoreTests(){Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,"NorcomLauncher.exe"),bootstrap);}
    private (VerifiedRelease release,string zip) Package(long sequence=1,string version="2.0.46",string bytes="candidate")
    {
        var path=Path.Combine(root,Guid.NewGuid().ToString("N")+".zip");
        var exe=Encoding.UTF8.GetBytes(bytes);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
        {
            using(var output=zip.CreateEntry("NorcomLauncher.exe").Open())output.Write(exe);
            using(var writer=new StreamWriter(zip.CreateEntry("payload-version.txt").Open()))writer.Write(version);
        }
        var now=DateTimeOffset.UtcNow;
        var p=new ReleasePayload(1,ReleaseVerifier.Product,ReleaseVerifier.Channel,"test-only",sequence,now.AddMinutes(-1),now.AddDays(1),version,1,
            "https://raw.githubusercontent.com/Jefftey97/norcom-pack/"+new string('a',40)+"/launcher/NorcomWpf-"+version+".zip",
            new FileInfo(path).Length,Hash(path),exe.Length,Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant());
        return(Verifier.Verify(SignedReleaseTests.Sign(key,JsonSerializer.Serialize(p,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),now),path);
    }
    private static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    [Fact] public void RejectsConcurrentWriterAndUnlockedMutation()
    {
        var store=new UpdateStore(root,Verifier);var p=Package();
        Assert.Throws<InvalidOperationException>(()=>store.Accept(p.release));
        using(var held=store.Acquire())
        {
            Assert.Throws<IOException>(()=>new UpdateStore(root,Verifier).Acquire());
            store.Accept(p.release);
        }
        using var acquired=new UpdateStore(root,Verifier).Acquire();
    }
    [Fact] public void PersistsSequenceAndRejectsReplayOrEquivocation()
    {
        var p=Package(2);var old=Package(1);var changed=Package(2,bytes:"different");
        var store=new UpdateStore(root,Verifier);
        using(store.Acquire()){store.Accept(p.release);store.Accept(p.release);}
        store=new UpdateStore(root,Verifier);
        using(store.Acquire())
        {
            Assert.Throws<InvalidDataException>(()=>store.Accept(old.release));
            Assert.Throws<InvalidDataException>(()=>store.Accept(changed.release));
            Assert.Equal(2,store.Read().Sequence);
        }
    }
    [Fact] public void AtomicStateReplacementKeepsAnExistingReadSnapshotUsable()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var first=Package(1);var next=Package(2);
        store.Accept(first.release);var path=Path.Combine(root,"update","state.json");
        using var snapshot=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        store.Accept(next.release);
        var old=JsonSerializer.Deserialize<UpdateState>(snapshot)!;
        Assert.Equal(1,old.Sequence);Assert.Equal(2,store.Read().Sequence);
    }
    [Fact] public void HealthyAttemptCommitsOnlyExactIdentity()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();
        var identity=store.Stage(p.release,p.zip);var attempt=store.Prepare(identity);
        Assert.Null(store.Read().Active);
        Assert.Throws<InvalidDataException>(()=>store.Commit(attempt.Id,identity.ExecutableHash,123,456));
        store.RecordLaunched(attempt.Id,123,456);
        Assert.Throws<InvalidDataException>(()=>store.Commit(Guid.NewGuid().ToString("N"),identity.ExecutableHash,123,456));
        Assert.Throws<InvalidDataException>(()=>store.Commit(attempt.Id,new string('0',64),123,456));
        Assert.Throws<InvalidDataException>(()=>store.Commit(attempt.Id,identity.ExecutableHash,124,456));
        Assert.Throws<InvalidDataException>(()=>store.Commit(attempt.Id,identity.ExecutableHash,123,457));
        store.Commit(attempt.Id,identity.ExecutableHash,123,456);
        Assert.Equal(identity,store.Read().Active);Assert.Null(store.Read().Pending);
        Assert.Equal(bootstrap,File.ReadAllBytes(store.BootstrapPath));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void InterruptedAttemptRollsBackAndQuarantinesWithoutLoweringWatermark(bool launched)
    {
        var p=Package(3);var store=new UpdateStore(root,Verifier);string attempt;PayloadIdentity identity;
        using(store.Acquire())
        {
            identity=store.Stage(p.release,p.zip);attempt=store.Prepare(identity).Id;
            if(launched)store.RecordLaunched(attempt,123,456);
        }
        store=new UpdateStore(root,Verifier);
        using(store.Acquire())
        {
            store.Rollback(attempt);
            var state=store.Read();Assert.Null(state.Active);Assert.Null(state.Pending);Assert.Equal(3,state.Sequence);
            Assert.Contains(identity.PackageHash,state.Quarantined);Assert.Equal("restored",state.Outcome);
            Assert.Throws<InvalidDataException>(()=>store.Prepare(identity));
            Assert.Equal(bootstrap,File.ReadAllBytes(store.BootstrapPath));
        }
    }
    [Fact] public void ReopeningBrokenCommittedPayloadRestoresPreviousVerifiedPayload()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var first=store.Stage(p.release,p.zip);
        var a=store.Prepare(first);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,first.ExecutableHash,1,2);
        p=Package(2,"2.0.47","next");var next=store.Stage(p.release,p.zip);a=store.Prepare(next);
        store.RecordLaunched(a.Id,3,4);store.Commit(a.Id,next.ExecutableHash,3,4);
        a=store.Prepare(next,reopening:true);store.Rollback(a.Id);
        Assert.Equal(first,store.Read().Active);Assert.Equal(2,store.Read().Sequence);
        Assert.True(store.Prepare(first,reopening:true).Recovery);
    }
    [Fact] public void NeverClaimsRestoredWhenBootstrapBytesChanged()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var attempt=store.Prepare(identity);File.WriteAllText(store.BootstrapPath,"changed");
        Assert.Throws<InvalidDataException>(()=>store.Rollback(attempt.Id));
        Assert.NotEqual("restored",store.Read().Outcome);Assert.NotNull(store.Read().Pending);
    }
    [Fact] public void RejectsTamperedPayloadAndUnacceptedReceipt()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        Assert.Throws<InvalidDataException>(()=>store.Prepare(identity with{PayloadDigest=new string('0',64)}));
        File.WriteAllText(store.PayloadPath(identity),"changed");
        Assert.Throws<InvalidDataException>(()=>store.Prepare(identity));
        Assert.Null(store.Read().Pending);
    }
    [Fact] public void RefusesPathEscapeInIdentity()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        Assert.Throws<InvalidDataException>(()=>store.PayloadPath(new PayloadIdentity("../../outside","x","2.0.46","x")));
    }
    [Fact] public void ReinstalledBootstrapIsRecordedUnderTheLock()
    {
        var store=new UpdateStore(root,Verifier);
        using(store.Acquire())
        {
            Assert.False(store.RefreshBootstrapHash());
            var before=store.Read().BootstrapHash;
            File.WriteAllBytes(Path.Combine(root,"NorcomLauncher.exe"),Encoding.UTF8.GetBytes("reinstalled bootstrap"));
            Assert.True(store.RefreshBootstrapHash());
            Assert.NotEqual(before,store.Read().BootstrapHash);
            Assert.False(store.RefreshBootstrapHash());
        }
        Assert.Throws<InvalidOperationException>(()=>store.RefreshBootstrapHash());
    }
    [Fact] public void StateReadIgnoresUnknownFieldsAndDefaultsOptionalOnes()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        var initial=store.Read();
        var path=Path.Combine(root,"update","state.json");
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        node.Remove("DeferredRetry");
        node["AddedByANewerLauncher"]=42;
        File.WriteAllText(path,node.ToJsonString());
        var read=store.Read();
        Assert.Equal(initial.InstallId,read.InstallId);
        Assert.Null(read.DeferredRetry);
        node.Remove("BootstrapHash");
        File.WriteAllText(path,node.ToJsonString());
        Assert.ThrowsAny<Exception>(()=>store.Read());
    }
    [Fact] public void DeletedWatermarkCannotBeSilentlyReinitialized()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();
        store.Stage(p.release,p.zip);File.Delete(Path.Combine(root,"update","state.json"));
        Assert.Throws<InvalidDataException>(()=>store.Read());
    }
    [Fact] public void ReplayedInitialSnapshotCannotEraseAnEstablishedPayloadHistory()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();store.Read();
        var path=Path.Combine(root,"update","state.json");var initial=File.ReadAllText(path);var p=Package();
        store.Stage(p.release,p.zip);File.WriteAllText(path,initial);
        Assert.Throws<InvalidDataException>(()=>store.Read());Assert.Equal(initial,File.ReadAllText(path));
    }
    [Fact] public void MalformedAttemptCannotSelectAndQuarantineTheSameActivePackage()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,identity.ExecutableHash,1,2);
        var invalid=store.Read() with{Outcome="starting",Pending=new LaunchAttempt(Guid.NewGuid().ToString("N"),identity,identity,"update","prepared")};
        var path=Path.Combine(root,"update","state.json");var bytes=JsonSerializer.Serialize(invalid);File.WriteAllText(path,bytes);
        Assert.Throws<InvalidDataException>(()=>store.Read());Assert.Equal(bytes,File.ReadAllText(path));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void RecoveryNeverStopsAnUnrelatedProcess(bool sameStartTime)
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);using var self=System.Diagnostics.Process.GetCurrentProcess();
        store.RecordLaunched(a.Id,self.Id,self.StartTime.ToUniversalTime().Ticks+(sameStartTime?0:1));
        var coordinator=new ActivationCoordinator();
        if(sameStartTime)
        {
            Assert.Throws<IOException>(()=>coordinator.RecoverInterrupted(store));Assert.NotNull(store.Read().Pending);
        }
        else{coordinator.RecoverInterrupted(store);Assert.Null(store.Read().Pending);}
        Assert.False(self.HasExited);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void RecoversCorruptOrMissingCommittedPayloadWithoutRequiringItToVerify(bool missing)
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,identity.ExecutableHash,1,2);
        if(missing)File.Delete(store.PayloadPath(identity));else File.WriteAllText(store.PayloadPath(identity),"corrupt");
        Assert.ThrowsAny<Exception>(()=>store.Prepare(identity,reopening:true));
        store.RecoverCommitted();Assert.Null(store.Read().Active);Assert.Equal("restored",store.Read().Outcome);
        Assert.Contains(identity.PackageHash,store.Read().Quarantined);Assert.Equal(1,store.Read().Sequence);
        Assert.Equal(bootstrap,File.ReadAllBytes(store.BootstrapPath));
    }
    [Theory][InlineData("duplicate")][InlineData("missing")][InlineData("sequence")]
    public void RejectsMalformedRecoveryState(string fault)
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,identity.ExecutableHash,1,2);
        var path=Path.Combine(root,"update","state.json");var text=File.ReadAllText(path);
        text=fault switch{
            "unknown"=>text[..^1]+",\"Unrecognized\":true}","duplicate"=>text[..^1]+",\"Schema\":1}",
            "missing"=>text.Replace("\"Previous\":null,",""),_=>text.Replace("\"Sequence\":1","\"Sequence\":0")};
        File.WriteAllText(path,text);
        Assert.ThrowsAny<Exception>(()=>store.Read());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void HealthyActiveCanReopenWhenOlderFallbackIsDamaged(bool missing)
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        var first=CommitPackage(store,1,"2.0.46","first");var active=CommitPackage(store,2,"2.0.47","second");
        if(missing)File.Delete(store.PayloadPath(first));else File.WriteAllText(store.PayloadPath(first),"corrupt");
        var attempt=store.Prepare(active,reopening:true);
        Assert.Equal(active,store.Read().Active);Assert.Null(attempt.Previous);Assert.Null(store.Read().Previous);
        store.Rollback(attempt.Id);Assert.Null(store.Read().Active);Assert.Equal("restored",store.Read().Outcome);
        Assert.Equal(bootstrap,File.ReadAllBytes(store.BootstrapPath));Assert.Equal(2,store.Read().Sequence);
    }
    [Fact] public void BrokenActiveAndPreviousCanRecoverToVerifiedBootstrap()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        var first=CommitPackage(store,1,"2.0.46","first");var active=CommitPackage(store,2,"2.0.47","second");
        File.WriteAllText(store.PayloadPath(first),"corrupt");File.WriteAllText(store.PayloadPath(active),"corrupt");
        store.RecoverCommitted();Assert.Null(store.Read().Active);Assert.Equal("restored",store.Read().Outcome);
        Assert.Contains(active.PackageHash,store.Read().Quarantined);Assert.Equal(2,store.Read().Sequence);
    }
    [Fact] public void FailedFallbackSelectionLeavesReceiptUnchanged()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        var first=CommitPackage(store,1,"2.0.46","first");var active=CommitPackage(store,2,"2.0.47","second");
        File.WriteAllText(store.PayloadPath(first),"corrupt");File.WriteAllText(store.BootstrapPath,"corrupt");
        var before=File.ReadAllBytes(Path.Combine(root,"update","state.json"));
        Assert.Throws<InvalidDataException>(()=>store.Prepare(active,reopening:true));
        Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"update","state.json")));
    }
    private PayloadIdentity CommitPackage(UpdateStore store,long sequence,string version,string bytes)
    {
        var p=Package(sequence,version,bytes);var identity=store.Stage(p.release,p.zip);var a=store.Prepare(identity);
        store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,identity.ExecutableHash,1,2);return identity;
    }
    [Fact] public void TransientReopenFailureCanBeExplicitlyRetriedWithoutPoisoningFallback()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();
        var first=CommitPackage(store,1,"2.0.46","first");var active=CommitPackage(store,2,"2.0.47","second");
        var a=store.Prepare(active,reopening:true);store.RecordLaunched(a.Id,1,2);store.Rollback(a.Id);
        Assert.Equal(first,store.Read().Active);Assert.DoesNotContain(active.PackageHash,store.Read().Quarantined);
        Assert.Null(store.Read().Previous);Assert.Equal(active,store.Read().DeferredRetry?.Candidate);
        Assert.True(store.Read().DeferredRetry?.PreviouslyHealthy);
        a=store.Prepare(first,reopening:true);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,first.ExecutableHash,1,2);
        Assert.NotNull(store.Read().DeferredRetry);
        a=store.Prepare(active);Assert.Equal("retry",a.Mode);store.RecordLaunched(a.Id,1,2);store.Commit(a.Id,active.ExecutableHash,1,2);
        Assert.Equal(active,store.Read().Active);Assert.Null(store.Read().DeferredRetry);
    }
    [Fact] public void UnlaunchedInterruptionIsRetryableButIsNotTreatedAsPreviouslyHealthy()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);store.Rollback(a.Id,RollbackReason.InterruptedBeforeLaunch);
        Assert.DoesNotContain(identity.PackageHash,store.Read().Quarantined);Assert.False(store.Read().DeferredRetry?.PreviouslyHealthy);
        a=store.Prepare(identity);Assert.Equal("update",a.Mode);store.RecordLaunched(a.Id,1,2);store.Rollback(a.Id);
        Assert.Contains(identity.PackageHash,store.Read().Quarantined);Assert.Null(store.Read().DeferredRetry);
    }
    [Fact] public void BootstrapRecoveryNoticeCanBeAcknowledgedWithoutResettingReplayState()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var identity=store.Stage(p.release,p.zip);
        var a=store.Prepare(identity);store.Rollback(a.Id);var before=store.Read();store.AcknowledgeBootstrapRecovery();var after=store.Read();
        Assert.Equal("ready",after.Outcome);Assert.Equal(before.Sequence,after.Sequence);Assert.Equal(before.AcceptedDigest,after.AcceptedDigest);
        Assert.Equal(before.Quarantined,after.Quarantined);Assert.Null(after.Active);
    }
    [Fact] public void RenewedSignedEnvelopeCanReuseExactPackageWithoutReplacingEarlierReceipt()
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var old=store.Stage(p.release,p.zip);
        var renewedPayload=p.release.Payload with{Sequence=2};
        var renewed=Verifier.Verify(SignedReleaseTests.Sign(key,JsonSerializer.Serialize(renewedPayload,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),DateTimeOffset.UtcNow);
        var current=store.Stage(renewed,p.zip);Assert.Equal(old.PackageHash,current.PackageHash);Assert.NotEqual(old.PayloadDigest,current.PayloadDigest);
        Assert.Equal(p.release.PayloadDigest,store.VerifyLocal(old).PayloadDigest);Assert.Equal(renewed.PayloadDigest,store.VerifyLocal(current).PayloadDigest);
        Assert.Equal(current,store.Prepare(current).Candidate);
    }
    [Theory][InlineData("version")][InlineData("package-size")][InlineData("exe-size")][InlineData("exe-hash")]
    public void RenewalCannotChangeImmutablePackageClaims(string fault)
    {
        var store=new UpdateStore(root,Verifier);using var held=store.Acquire();var p=Package();var old=store.Stage(p.release,p.zip);
        var payload=p.release.Payload with{Sequence=2};
        payload=fault switch{
            "version"=>payload with{Version="2.0.47"},
            "package-size"=>payload with{PackageBytes=payload.PackageBytes+1},
            "exe-size"=>payload with{ExecutableBytes=payload.ExecutableBytes+1},
            _=>payload with{ExecutableSha256=new string('a',64)}};
        var renewed=Verifier.Verify(SignedReleaseTests.Sign(key,JsonSerializer.Serialize(payload,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),DateTimeOffset.UtcNow);
        Assert.Throws<InvalidDataException>(()=>store.Stage(renewed,p.zip));
        Assert.Equal(old.PayloadDigest,store.VerifyLocal(old).PayloadDigest);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(store.PayloadPath(old))!,"release-"+renewed.PayloadDigest+".json")));
    }
    public void Dispose(){key.Dispose();Directory.Delete(root,true);}
}
