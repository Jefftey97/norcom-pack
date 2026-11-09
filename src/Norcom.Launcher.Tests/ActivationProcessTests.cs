using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class ActivationProcessTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"norcom-activation-"+Guid.NewGuid().ToString("N"),"NORCOM","Launcher");
    private readonly ECDsa key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public ActivationProcessTests()=>Directory.CreateDirectory(root);
    [Fact] public async Task SingleInstanceShutdownReleasesListenerAndMutex()
    {
        var fixture=Environment.GetEnvironmentVariable("LAUNCHER_TEST_FIXTURE");
        Assert.True(File.Exists(fixture));
        using var process=Process.Start(new ProcessStartInfo(fixture!,"--fixture-instance-cycle"){UseShellExecute=false,CreateNoWindow=true})!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));Assert.Equal(0,process.ExitCode);
    }
    [Theory][InlineData("healthy",true)][InlineData("crash-before-main",false)]
    [InlineData("crash-after-authorized",false)][InlineData("wrong-nonce",false)][InlineData("hang-after-authorized",false)]
    [InlineData("invalid-native-image",false)]
    public async Task NativeCandidateMustCompleteExactStartupHandshake(string mode,bool expected)
    {
        var (store,identity,_)=PrepareFixture(mode);
        using var held=store.Acquire();
        var saved=Path.Combine(Path.GetDirectoryName(root)!,"auth");
        var result=await new ActivationCoordinator().LaunchPendingAsync(store,TimeSpan.FromSeconds(mode=="healthy"?30:3),CancellationToken.None);
        Assert.Equal(expected?ActivationOutcome.Committed:ActivationOutcome.Restored,result);
        var state=store.Read();Assert.Null(state.Pending);
        Assert.Equal(expected?identity:null,state.Active);
        Assert.Equal(expected?"healthy":"restored",state.Outcome);
        if(!expected)Assert.Contains(identity.PackageHash,state.Quarantined);
        Assert.Equal("known-good bootstrap fixture; never execute",File.ReadAllText(store.BootstrapPath));
        Assert.Equal("unchanged protected bytes",File.ReadAllText(Path.Combine(saved,"session.bin")));
        // Healthy fixture exits normally after receiving the durable commit receipt.
        if(expected)await WaitUntilAsync(()=>File.Exists(Path.Combine(root,"fixture-committed.txt")),TimeSpan.FromSeconds(10));
        await AssertChildExitedAsync();
    }
    [Theory][InlineData("prepared")][InlineData("authorized")]
    public async Task CoordinatorDeathIsRecoveredWithoutLeavingAnUnreadyProcess(string phase)
    {
        var (store,identity,fixture)=PrepareFixture("hang-after-authorized");
        var info=new ProcessStartInfo(fixture){UseShellExecute=false,CreateNoWindow=true};
        info.ArgumentList.Add("--fixture-supervisor");info.ArgumentList.Add(root);info.ArgumentList.Add(phase);
        using var coordinator=Process.Start(info)!;
        try
        {
            var marker=Path.Combine(root,phase=="prepared"?"fixture-process.json":"fixture-authorized.txt");
            await WaitUntilAsync(()=>File.Exists(marker),TimeSpan.FromSeconds(30));
            coordinator.Kill(entireProcessTree:false);
            await coordinator.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if(phase=="authorized")await AssertChildExitedAsync(); // Watchdog must exit without recovery killing it.
            using(await AcquireAfterExitAsync(store))
            {
                new ActivationCoordinator().RecoverInterrupted(store);
                Assert.Equal("restored",store.Read().Outcome);
                Assert.Null(store.Read().Pending);Assert.Null(store.Read().Active);
                if(phase=="prepared")
                {
                    Assert.DoesNotContain(identity.PackageHash,store.Read().Quarantined);
                    Assert.Equal(identity,store.Read().DeferredRetry?.Candidate);
                }
                else Assert.Contains(identity.PackageHash,store.Read().Quarantined);
                Assert.Equal(1,store.Read().Sequence);
            }
            await AssertChildExitedAsync();
            Assert.False(File.Exists(Path.Combine(root,"fixture-committed.txt")));
        }
        finally
        {
            if(!coordinator.HasExited){coordinator.Kill(entireProcessTree:true);await coordinator.WaitForExitAsync();}
        }
    }
    [Fact] public async Task LostCommitAcknowledgementNeverKillsAnAlreadyCommittedCandidate()
    {
        var (store,identity,_)=PrepareFixture("disconnect-after-ready");using var held=store.Acquire();
        var outcome=await new ActivationCoordinator().LaunchPendingAsync(store,TimeSpan.FromSeconds(30),CancellationToken.None);
        Assert.True(outcome is ActivationOutcome.Committed or ActivationOutcome.CommittedNotificationUncertain);
        Assert.Equal(identity,store.Read().Active);Assert.Null(store.Read().Pending);
        await WaitUntilAsync(()=>File.Exists(Path.Combine(root,"fixture-survived-disconnect.txt")),TimeSpan.FromSeconds(10));
        await AssertChildExitedAsync();
    }
    [Fact] public async Task ErrorAfterDurableCommitDoesNotStopOrRestoreTheHealthyCandidate()
    {
        var (store,identity,_)=PrepareFixture("healthy");using var held=store.Acquire();
        var coordinator=new ActivationCoordinator((s,a,c)=>
        {
            s.Commit(a.Id,a.Candidate.ExecutableHash,c.Id,a.StartedUtcTicks!.Value);
            throw new IOException("Injected caller error after durable commit");
        });
        var outcome=await coordinator.LaunchPendingAsync(store,TimeSpan.FromSeconds(30),CancellationToken.None);
        Assert.Equal(ActivationOutcome.CommittedNotificationUncertain,outcome);Assert.Equal(identity,store.Read().Active);
        Assert.Equal("healthy",store.Read().Outcome);Assert.Empty(store.Read().Quarantined);
        await WaitUntilAsync(()=>File.Exists(Path.Combine(root,"fixture-committed.txt")),TimeSpan.FromSeconds(10));
        await AssertChildExitedAsync();
    }
    [Fact] public void DefaultProductionPayloadDoesNotBecomeADisposableProfile()
    {
        var local=Path.Combine(Path.GetTempPath(),"normal-user-local");var installed=Path.Combine(local,"NORCOM","Launcher");
        Assert.Null(ActivationCoordinator.ChildStateOverride(installed,null,local));
        Assert.Equal(Path.GetDirectoryName(installed),ActivationCoordinator.ChildStateOverride(installed,"explicit-owner-profile",local));
        Assert.Equal(Path.GetDirectoryName(root),ActivationCoordinator.ChildStateOverride(root,null,local));
    }
    private async Task AssertChildExitedAsync()
    {
        var path=Path.Combine(root,"fixture-process.json");
        if(!File.Exists(path))return; // A candidate may fail before any managed entry point.
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        var pid=doc.RootElement.GetProperty("pid").GetInt32();var ticks=doc.RootElement.GetProperty("ticks").GetInt64();
        await WaitUntilAsync(()=>
        {
            try{using var process=Process.GetProcessById(pid);return process.HasExited||process.StartTime.ToUniversalTime().Ticks!=ticks;}
            catch(ArgumentException){return true;}
        },TimeSpan.FromSeconds(10));
    }
    private static async Task WaitUntilAsync(Func<bool> condition,TimeSpan budget)
    {
        var timeout=Stopwatch.StartNew();
        while(!condition()){Assert.True(timeout.Elapsed<budget,"Native fixture did not reach the bounded expected state.");await Task.Delay(30);}
    }
    private static async Task<IDisposable> AcquireAfterExitAsync(UpdateStore store)
    {
        // Windows may release inherited/runtime handles just after the process exit
        // notification. Match the real bootstrap's bounded sharing-violation retry.
        var timer=Stopwatch.StartNew();
        while(true)
        {
            try{return store.Acquire();}
            catch(IOException ex) when((ex.HResult & 0xffff)==32 && timer.Elapsed<TimeSpan.FromSeconds(3))
            {await Task.Delay(50);}
        }
    }
    private (UpdateStore store,PayloadIdentity identity,string fixture) PrepareFixture(string mode)
    {
        var fixture=Environment.GetEnvironmentVariable("LAUNCHER_TEST_FIXTURE");
        Assert.True(File.Exists(fixture),"Build the isolated UpdateFixture and set LAUNCHER_TEST_FIXTURE before this integration suite.");
        File.WriteAllText(Path.Combine(root,"NorcomLauncher.exe"),"known-good bootstrap fixture; never execute");
        var saved=Path.Combine(Path.GetDirectoryName(root)!,"auth");Directory.CreateDirectory(saved);
        File.WriteAllText(Path.Combine(saved,"session.bin"),"unchanged protected bytes");
        File.WriteAllText(Path.Combine(root,"fixture-mode.txt"),mode);
        File.WriteAllText(Path.Combine(root,"fixture-public.pem"),key.ExportSubjectPublicKeyInfoPem());
        var exe=mode=="invalid-native-image"?Encoding.UTF8.GetBytes("Not a Windows executable"):File.ReadAllBytes(fixture!);var package=Path.Combine(root,"fixture.zip");
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
        {
            using(var output=zip.CreateEntry("NorcomLauncher.exe").Open())output.Write(exe);
            using(var writer=new StreamWriter(zip.CreateEntry("payload-version.txt").Open()))writer.Write("2.0.46");
        }
        var now=DateTimeOffset.UtcNow;
        var payload=new ReleasePayload(1,ReleaseVerifier.Product,ReleaseVerifier.Channel,"test-only",1,now.AddMinutes(-1),now.AddDays(1),"2.0.46",1,
            "https://raw.githubusercontent.com/Jefftey97/norcom-pack/"+new string('a',40)+"/launcher/NorcomWpf-2.0.46.zip",
            new FileInfo(package).Length,Hash(File.ReadAllBytes(package)),exe.Length,Hash(exe));
        var verifier=new ReleaseVerifier(new Dictionary<string,string>{{"test-only",key.ExportSubjectPublicKeyInfoPem()}});
        var release=verifier.Verify(SignedReleaseTests.Sign(key,JsonSerializer.Serialize(payload,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),now);
        var store=new UpdateStore(root,verifier);using var held=store.Acquire();
        var identity=store.Stage(release,package);store.Prepare(identity);
        return (store, identity, fixture!);
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public void Dispose()
    {
        key.Dispose();
        var directory=Path.GetDirectoryName(Path.GetDirectoryName(root)!)!;
        var until=Stopwatch.StartNew();
        // Assertions above require the exact child to exit. Windows may retain
        // its image mapping briefly afterward; retry cleanup without hiding failure.
        while(true)
        {
            try{Directory.Delete(directory,true);return;}
            catch(Exception ex) when((ex is IOException || ex is UnauthorizedAccessException) &&
                ((ex.HResult & 0xffff) is 5 or 32) && until.Elapsed<TimeSpan.FromSeconds(5))
            {Thread.Sleep(50);}
        }
    }
}
