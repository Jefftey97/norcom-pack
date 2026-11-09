using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class ReleasePackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),"norcom-package-tests-"+Guid.NewGuid().ToString("N"));
    private readonly ECDsa key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] exe=Encoding.UTF8.GetBytes("fake native executable; never execute");
    public ReleasePackageTests()=>Directory.CreateDirectory(root);
    private byte[] Zip(string? extra=null, bool duplicate=false, bool symlink=false, string version="2.0.46")
    {
        using var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            var entry=zip.CreateEntry("NorcomLauncher.exe");
            if(symlink) entry.ExternalAttributes=unchecked((int)0xA1FF0000);
            using(var output=entry.Open()) output.Write(exe);
            using(var writer=new StreamWriter(zip.CreateEntry("payload-version.txt").Open())) writer.Write(version);
            if(extra!=null) zip.CreateEntry(extra);
            if(duplicate) zip.CreateEntry("NorcomLauncher.exe");
        }
        return stream.ToArray();
    }
    private VerifiedRelease Release(byte[] package,long? size=null,string? hash=null)
    {
        var now=DateTimeOffset.UtcNow;
        var p=new ReleasePayload(1,ReleaseVerifier.Product,ReleaseVerifier.Channel,"test-only",1,now.AddMinutes(-1),now.AddDays(1),"2.0.46",1,
            "https://raw.githubusercontent.com/Jefftey97/norcom-pack/"+new string('a',40)+"/launcher/NorcomWpf-2.0.46.zip",
            size??package.Length,hash??Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(),exe.Length,Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant());
        var verifier=new ReleaseVerifier(new Dictionary<string,string>{{"test-only",key.ExportSubjectPublicKeyInfoPem()}});
        return verifier.Verify(SignedReleaseTests.Sign(key,JsonSerializer.Serialize(p,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})),now);
    }
    [Fact] public void ExtractsOnlyVerifiedExecutableAndVersion()
    {
        var bytes=Zip(); var package=Path.Combine(root,"package.zip"); File.WriteAllBytes(package,bytes);
        var extracted=ReleasePackage.Extract(Release(bytes),package,Path.Combine(root,"stage"));
        Assert.Equal(exe,File.ReadAllBytes(extracted));
        Assert.Equal(2,Directory.GetFiles(Path.GetDirectoryName(extracted)!).Length);
    }
    [Theory][InlineData("../outside.exe")][InlineData("OTHER.exe")][InlineData("nested/NorcomLauncher.exe")][InlineData("NORCOMLAUNCHER.EXE")]
    public void RejectsUnexpectedOrAliasedArchiveEntries(string extra)=>RejectZip(Zip(extra));
    [Fact] public void RejectsDuplicateEntry()=>RejectZip(Zip(duplicate:true));
    [Fact] public void RejectsSymlinkEntry()=>RejectZip(Zip(symlink:true));
    [Fact] public void RejectsWrongPayloadVersion()=>RejectZip(Zip(version:"2.0.47"));
    private void RejectZip(byte[] bytes)
    {
        var package=Path.Combine(root,"package.zip");File.WriteAllBytes(package,bytes);
        Assert.Throws<InvalidDataException>(()=>ReleasePackage.Extract(Release(bytes),package,Path.Combine(root,"stage")));
        Assert.False(File.Exists(Path.Combine(root,"outside.exe")));
    }
    [Fact] public void RechecksPackageBytesBeforeExtracting()
    {
        var bytes=Zip();var release=Release(bytes);var package=Path.Combine(root,"package.zip");
        File.WriteAllBytes(package,Zip(version:"2.0.47"));
        Assert.Throws<InvalidDataException>(()=>ReleasePackage.Extract(release,package,Path.Combine(root,"stage")));
    }
    [Theory][InlineData(-1)][InlineData(1)][InlineData(0)]
    public async Task RejectsWrongStreamLengthOrHash(int difference)
    {
        var bytes=Zip();var release=Release(bytes,bytes.Length+difference,difference==0?new string('0',64):null);
        using var http=new HttpClient(new Handler(bytes));
        var dest=Path.Combine(root,"update.zip");
        await Assert.ThrowsAsync<InvalidDataException>(()=>ReleasePackage.DownloadAsync(http,release,dest,CancellationToken.None));
        Assert.False(File.Exists(dest));
    }
    [Fact] public async Task StreamsValidPackageAndRefusesRedirect()
    {
        var bytes=Zip();var dest=Path.Combine(root,"update.zip");
        using var http=new HttpClient(new Handler(bytes));
        await ReleasePackage.DownloadAsync(http,Release(bytes),dest,CancellationToken.None);
        Assert.Equal(bytes,File.ReadAllBytes(dest));
        using var redirect=new HttpClient(new Handler(bytes,HttpStatusCode.Redirect));
        await Assert.ThrowsAsync<InvalidDataException>(()=>ReleasePackage.DownloadAsync(redirect,Release(bytes),Path.Combine(root,"redirect.zip"),CancellationToken.None));
    }
    private sealed class Handler(byte[] bytes,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            // Deliberately omit Content-Length so the streaming bound itself is tested.
            var response=new HttpResponseMessage(status){RequestMessage=request,Content=new StreamContent(new Unseekable(bytes))};
            Assert.Null(response.Content.Headers.ContentLength);
            return Task.FromResult(response);
        }
    }
    private sealed class Unseekable(byte[] bytes):MemoryStream(bytes){public override bool CanSeek=>false;}
    public void Dispose(){key.Dispose();Directory.Delete(root,true);}
}
