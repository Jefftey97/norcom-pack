using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Updates;

// Disposable process-test fixture only. Never included in a release package.
if(args.Length==1 && args[0]=="--fixture-instance-cycle")
{
    var scope="NORCOM.Launcher.Check."+Guid.NewGuid().ToString("N");
    for(var i=0;i<100;i++)
    {
        using(var instance=new Norcom.Launcher.Services.SingleInstance(scope))
        {
            if(!instance.Acquire())return 30;
            // Exercise signal/close while the real listener is blocked in WaitOne.
            using var signal=EventWaitHandle.OpenExisting("Global\\"+scope+".Activate");signal.Set();
        }
        var acquired=Task.Run(()=>
        {
            using var next=new Norcom.Launcher.Services.SingleInstance(scope);return next.Acquire();
        }).GetAwaiter().GetResult();
        if(!acquired)return 31;
    }
    Console.WriteLine("100 activate/close/reacquire cycles passed.");return 0;
}
if(args.Length==5 && args[0]=="--fixture-stage")
{
    var trust=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))!;
    var stagingVerifier=new ReleaseVerifier(trust);var store=new UpdateStore(args[1],stagingVerifier);using var lease=store.Acquire();
    var release=stagingVerifier.Verify(File.ReadAllText(args[3]),DateTimeOffset.UtcNow);
    var identity=store.Stage(release,args[4]);Console.WriteLine(store.Prepare(identity).Id);return 0;
}
if(args.Length==3 && args[0]=="--fixture-supervisor")
{
    var parentRoot=args[1];
    var trust=new ReleaseVerifier(new Dictionary<string,string>{{"test-only",File.ReadAllText(Path.Combine(parentRoot,"fixture-public.pem"))}});
    var parentStore=new UpdateStore(parentRoot,trust);using var held=parentStore.Acquire();
    if(args[2]=="prepared")
    {
        var attempt=parentStore.Read().Pending!;
        var name="norcom-launcher-"+attempt.Id+"-"+Guid.NewGuid().ToString("N");
        using var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var launch=new ProcessStartInfo(parentStore.PayloadPath(attempt.Candidate)){UseShellExecute=false,CreateNoWindow=true};
        launch.Environment["NORCOM_STATE_DIR"]=Path.GetDirectoryName(parentRoot)!;
        foreach(var item in new[]{"--norcom-payload",attempt.Id,name,new string('a',64)})launch.ArgumentList.Add(item);
        using var child=Process.Start(launch)!;
        // Inject coordinator death after Process.Start but before RecordLaunched.
        await Task.Delay(60000);
    }
    else await new ActivationCoordinator().LaunchPendingAsync(parentStore,TimeSpan.FromSeconds(60),CancellationToken.None);
    return 0;
}
if(args.Length!=4 || args[0]!="--norcom-payload")return 2;
var root=NorcomPaths.LauncherDir;
var mode=File.ReadAllText(Path.Combine(root,"fixture-mode.txt"));
using(var self=Process.GetCurrentProcess())File.WriteAllText(Path.Combine(root,"fixture-process.json"),JsonSerializer.Serialize(new{pid=self.Id,ticks=self.StartTime.ToUniversalTime().Ticks}));
if(mode=="crash-before-main")return 7;
var verifier=new ReleaseVerifier(new Dictionary<string,string>{{"test-only",File.ReadAllText(Path.Combine(root,"fixture-public.pem"))}});
if(mode=="disconnect-after-ready")
{
    using var pipe=new NamedPipeClientStream(".",args[2],PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await pipe.ConnectAsync(timeout.Token);
    using var self=Process.GetCurrentProcess();
    var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))).ToLowerInvariant();
    var identity=new ActivationMessage("hello",args[1],args[3],hash,self.Id,self.StartTime.ToUniversalTime().Ticks);
    await SendFrame(pipe,identity,timeout.Token);
    var header=new byte[4];await pipe.ReadExactlyAsync(header,timeout.Token);
    var reply=new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header)];await pipe.ReadExactlyAsync(reply,timeout.Token);
    if(JsonSerializer.Deserialize<ActivationMessage>(reply)!=identity with{Stage="authorized"})return 10;
    await SendFrame(pipe,identity with{Stage="ready"},timeout.Token);
    pipe.Dispose();
    await Task.Delay(1000);
    File.WriteAllText(Path.Combine(root,"fixture-survived-disconnect.txt"),"postcommit process was not killed");
    await Task.Delay(250);return 0;
}
try
{
    using var client=await ActivationClient.AuthorizeAsync(new UpdateStore(root,verifier),args[1],args[2],
        mode=="wrong-nonce"?new string('0',64):args[3],CancellationToken.None);
    File.WriteAllText(Path.Combine(root,"fixture-authorized.txt"),"authorized");
    if(mode=="crash-after-authorized")return 8;
    if(mode=="hang-after-authorized")await Task.Delay(60000);
    await client.ReadyAsync(CancellationToken.None);
    File.WriteAllText(Path.Combine(root,"fixture-committed.txt"),"authorized and committed");
    await Task.Delay(250);
    return 0;
}
catch{return 9;}

static async Task SendFrame(Stream stream,ActivationMessage message,CancellationToken ct)
{
    var bytes=JsonSerializer.SerializeToUtf8Bytes(message);var header=new byte[4];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
    await stream.WriteAsync(header,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
}
