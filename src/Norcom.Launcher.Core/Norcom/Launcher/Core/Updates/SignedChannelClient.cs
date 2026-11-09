#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Updates;

public sealed class SignedChannelClient:IDisposable
{
    public const string Feed="https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/launcher/channel-v1.json";
    private readonly HttpClient _http;
    private readonly ReleaseVerifier _verifier;
    public SignedChannelClient(ReleaseVerifier verifier)
    {
        _verifier=verifier;
        _http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None})
            {Timeout=TimeSpan.FromMinutes(3)};
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NORCOM-Launcher-Bridge/2.0.45");
    }
    public async Task<VerifiedRelease?> FetchAsync(CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response=await _http.GetAsync(Feed,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        if(response.StatusCode!=HttpStatusCode.OK || response.RequestMessage?.RequestUri?.AbsoluteUri!=Feed ||
            response.Content.Headers.ContentLength>ReleaseVerifier.MaximumEnvelopeBytes)
            throw new InvalidDataException("Signed update channel is unavailable or exceeds its limit.");
        await using var input=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var bytes=new MemoryStream();var buffer=new byte[4096];int count;
        while((count=await input.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0)
        {
            if(bytes.Length+count>ReleaseVerifier.MaximumEnvelopeBytes)throw new InvalidDataException("Signed update channel exceeds its limit.");
            bytes.Write(buffer,0,count);
        }
        return _verifier.Verify(new UTF8Encoding(false,true).GetString(bytes.ToArray()),DateTimeOffset.UtcNow);
    }
    public async Task DownloadAsync(VerifiedRelease release,string destination,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await ReleasePackage.DownloadAsync(_http,release,destination,timeout.Token).ConfigureAwait(false);
    }
    public void Dispose()=>_http.Dispose();
}
