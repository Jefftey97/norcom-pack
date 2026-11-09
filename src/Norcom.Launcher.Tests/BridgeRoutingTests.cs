using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class BridgeRoutingTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task BridgeRouteNeverFallsBackToUnsignedManifestEvenWhenNoUpdateIsScheduled(bool scheduled)
    {
        var previous=BridgeUpdateRouting.Schedule;var calls=0;
        try
        {
            BridgeUpdateRouting.Schedule=_=>{calls++;return Task.FromResult(scheduled);};
            using var http=new HttpClient(new ForbiddenNetwork());
            Assert.Equal(scheduled,await new SelfUpdateService(http).TrySchedulePlayUpdateAsync(CancellationToken.None));
            Assert.Equal(1,calls);
        }
        finally{BridgeUpdateRouting.Schedule=previous;}
    }
    private sealed class ForbiddenNetwork:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
            =>throw new InvalidOperationException("The legacy unsigned route must not run.");
    }
}
