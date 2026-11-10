using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;
public class DesktopResilienceTests
{
    [Theory][InlineData(683,344)][InlineData(480,320)][InlineData(1920,1040)]
    public void WindowFitsSmallHighDpiWorkAreas(double width,double height)
    {
        var bounds=WindowSizing.Fit(1180,800,width,height);
        Assert.InRange(bounds.Width,1,width);Assert.InRange(bounds.Height,1,height);
        Assert.InRange(bounds.MinWidth,1,bounds.Width);Assert.InRange(bounds.MinHeight,1,bounds.Height);
    }
    [Fact] public async Task SilentConnectedPeerHasABoundedStatusDeadline()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var watch=Stopwatch.StartNew();var ping=ServerStatusClient.PingAsync("127.0.0.1",port,0,CancellationToken.None);
        using var peer=await listener.AcceptTcpClientAsync();
        var timeout=Task.Delay(TimeSpan.FromSeconds(4));
        if(await Task.WhenAny(ping,timeout)!=ping)peer.Dispose();
        var result=await ping.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(result.Online);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(3.5),"A silent peer must not hold status initialization indefinitely");
    }
    [Theory][InlineData("{\"up\":true,\"players\":4}")][InlineData("{\"up\":true,\"updatedUtc\":\"2026-11-06T03:00:00Z\"}")]
    public void MissingOrStaleStatusTimestampNeverShowsLivePlayers(string json)
    {
        var status=HomeStatusClient.Parse(json,"fixture");
        Assert.False(status.ShowOnline);Assert.Equal("Unknown",status.Headline);Assert.Equal("—",status.PlayersText);
    }
    [Fact] public async Task OversizedPeerFrameIsRejectedBeforeWaitingForItsBody()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var ping=ServerStatusClient.PingAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,0,CancellationToken.None);
        using var peer=await listener.AcceptTcpClientAsync();
        // 1,048,577 byte frame. The peer deliberately supplies no body.
        await peer.GetStream().WriteAsync(new byte[]{0x81,0x80,0x40});
        var completed=await Task.WhenAny(ping,Task.Delay(1000))==ping;
        peer.Dispose();await ping.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(completed,"Oversized peer-controlled lengths must be refused before body allocation/read");
    }
}
