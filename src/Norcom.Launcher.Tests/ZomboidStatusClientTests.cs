using System.Net;
using System.Net.Sockets;
using System.Text;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ZomboidStatusClientTests
{
	private static byte[] InfoReply(int players, int max)
	{
		var bytes = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11 };
		foreach (string s in new[] { "NORCOM Zomboid", "Muldraugh, KY", "zomboid", "Project Zomboid" })
		{
			bytes.AddRange(Encoding.UTF8.GetBytes(s));
			bytes.Add(0);
		}
		bytes.AddRange(new byte[] { 0x00, 0x00 }); // app id
		bytes.Add((byte)players);
		bytes.Add((byte)max);
		bytes.AddRange(new byte[] { 0, (byte)'d', (byte)'w', 0, 1 });
		return bytes.ToArray();
	}

	[Fact]
	public void ParsesPlayersFromAnInfoReply()
	{
		ZomboidStatus? status = ZomboidStatusClient.ParseInfo(InfoReply(3, 32));
		Assert.NotNull(status);
		Assert.True(status!.Online);
		Assert.Equal(3, status.Players);
		Assert.Equal(32, status.MaxPlayers);
	}

	[Fact]
	public void RejectsAnythingThatIsNotAnInfoReply()
	{
		Assert.Null(ZomboidStatusClient.ParseInfo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x41, 1, 2, 3, 4 }));
		Assert.Null(ZomboidStatusClient.ParseInfo(new byte[] { 1, 2, 3 }));
		Assert.Null(ZomboidStatusClient.ParseInfo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x11, (byte)'x' }));
	}

	[Fact]
	public async Task AnswersAChallengeThenReadsTheReply()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
		var fake = Task.Run(async () =>
		{
			var first = await server.ReceiveAsync();
			Assert.Equal(0x54, first.Buffer[4]);
			await server.SendAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x41, 9, 8, 7, 6 }, 9, first.RemoteEndPoint);
			var second = await server.ReceiveAsync();
			Assert.Equal(new byte[] { 9, 8, 7, 6 }, second.Buffer[^4..]);
			byte[] reply = InfoReply(5, 32);
			await server.SendAsync(reply, reply.Length, second.RemoteEndPoint);
		});
		ZomboidStatus status = await ZomboidStatusClient.QueryAsync("127.0.0.1", port, TimeSpan.FromSeconds(2), CancellationToken.None);
		await fake;
		Assert.True(status.Online);
		Assert.Equal(5, status.Players);
	}

	[Fact]
	public async Task SilentServerIsOffline()
	{
		using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
		ZomboidStatus status = await ZomboidStatusClient.QueryAsync("127.0.0.1", port, TimeSpan.FromMilliseconds(200), CancellationToken.None);
		Assert.False(status.Online);
	}
}
