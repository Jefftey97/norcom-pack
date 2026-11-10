using System.Net;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;
public class DiagnosticConsentTests
{
    [Fact] public async Task LegacyFallbackCannotFlushNewPrivateReportsButExplicitSendStillWorks()
    {
        var previous=Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
        var profile=Path.Combine(Path.GetTempPath(),"diagnostic-consent-"+Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("NORCOM_STATE_DIR",profile);
            var legacy=Path.Combine(NorcomPaths.LauncherDir,"outbox");Directory.CreateDirectory(legacy);
            var report=CrashReportService.Queue("privacy-test","saved without consent",null);
            using var handler=new FakeReceiver();using var http=new HttpClient(handler);
            const string endpoint="https://discord.com/api/webhooks/test-only/not-a-secret";
            // Flush implementation is unchanged from550; the legacy binary passes
            // its compiled-in Launcher/outbox and enumerates only that directory.
            await CrashReportService.FlushAsync(http,legacy,endpoint,null,"test",null,CancellationToken.None);
            Assert.Equal(0,handler.Posts);Assert.True(File.Exists(report));
            Assert.Equal("Saved",File.ReadAllText(report+".status"));
            var sent=await CrashReportService.TryDeliverAsync(http,report,endpoint,CancellationToken.None);
            Assert.Equal(CrashReportService.DeliveryKind.Sent,sent.Kind);Assert.Equal(1,handler.Posts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NORCOM_STATE_DIR",previous);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(profile));
            if(Directory.Exists(profile))Directory.Delete(profile,true);
        }
    }
    private sealed class FakeReceiver:HttpMessageHandler
    {
        public int Posts {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Posts++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}
    }
}
