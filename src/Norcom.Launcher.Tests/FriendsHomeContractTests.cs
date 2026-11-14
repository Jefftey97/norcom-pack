using System.Text;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class FriendsHomeContractTests
{
	private const string LiveSrv = "4E43818000010001000000010A5F6D696E656372616674045F7463700A736872696D702D6375620374756E03706C790267670000210001C00C002100010000012C001D00010001EB990A736872696D702D6375620374756E03706C790267670000002904D0000000000000";

	[Fact]
	public void LiveSrvAnswerIsThePlayitPortNot25565()
	{
		IReadOnlyList<SrvTarget> records = FriendsServer.ParseSrv(Convert.FromHexString(LiveSrv));
		FriendsEndpoint? endpoint = FriendsServer.Pick(records);
		Assert.NotNull(endpoint);
		Assert.Equal("shrimp-cub.tun.ply.gg", endpoint.Value.Host);
		Assert.Equal(60313, endpoint.Value.Port);
		Assert.Equal("srv", endpoint.Value.Kind);
		Assert.DoesNotContain(":25565", endpoint.Value.Display);
	}

	[Fact]
	public void LowerPrioritySrvWinsOverA25565Record()
	{
		SrvTarget[] records = new[]
		{
			new SrvTarget(10, 5, 25565, "shrimp-cub.tun.ply.gg"),
			new SrvTarget(1, 1, 60313, "shrimp-cub.tun.ply.gg")
		};
		FriendsEndpoint endpoint = FriendsServer.Resolve(hostPc: false, () => records);
		Assert.Equal("shrimp-cub.tun.ply.gg:60313", endpoint.Display);
		Assert.Equal("srv", endpoint.Kind);
	}

	[Fact]
	public void MissingSrvFallsBackToTheCurrentPlayitPort()
	{
		FriendsEndpoint endpoint = FriendsServer.Resolve(hostPc: false, () => Array.Empty<SrvTarget>());
		Assert.Equal(FriendsServer.FallbackDisplay, endpoint.Display);
		Assert.Equal("fallback", endpoint.Kind);
		Assert.Equal("shrimp-cub.tun.ply.gg:25344", endpoint.Display);
		Assert.DoesNotContain(":25565", endpoint.Display);
	}

	[Fact]
	public void HostPcUsesLoopback25565AndDoesNotQuerySrv()
	{
		bool queried = false;
		FriendsEndpoint endpoint = FriendsServer.Resolve(hostPc: true, () =>
		{
			queried = true;
			return new[] { new SrvTarget(0, 0, 60313, FriendsServer.PublicHost) };
		});
		Assert.False(queried);
		Assert.Equal("127.0.0.1:25565", endpoint.Display);
		Assert.Equal("host-loopback", endpoint.Kind);
	}

	[Fact]
	public void HostPcFlagOverridesThePublicHostname()
	{
		string? previous = Environment.GetEnvironmentVariable(FriendsServer.HostPcVariable);
		try
		{
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, "1");
			Assert.True(FriendsServer.IsHostPc());
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, "0");
			Assert.False(FriendsServer.IsHostPc());
		}
		finally
		{
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, previous);
		}
	}

	[Fact]
	public void NewServerListUsesTheResolvedPort()
	{
		string? previous = Environment.GetEnvironmentVariable(FriendsServer.HostPcVariable);
		string friendsDir = Path.Combine(Path.GetTempPath(), "norcom-srv-" + Guid.NewGuid().ToString("n"));
		string hostDir = Path.Combine(Path.GetTempPath(), "norcom-host-" + Guid.NewGuid().ToString("n"));
		try
		{
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, "0");
			ServersDat.Ensure(friendsDir, "NORCOM", "shrimp-cub.tun.ply.gg");
			string friends = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(friendsDir, "servers.dat")));
			// The port comes from live DNS (or the fallback), so assert the shape, not a fixed number.
			Assert.Matches(@"shrimp-cub\.tun\.ply\.gg:\d{2,5}", friends);
			Assert.DoesNotContain(":25565", friends);
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, "1");
			ServersDat.Ensure(hostDir, "NORCOM", "shrimp-cub.tun.ply.gg");
			string host = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(hostDir, "servers.dat")));
			Assert.Contains("127.0.0.1:25565", host);
			Assert.DoesNotContain("shrimp-cub.tun.ply.gg:25565", host);
		}
		finally
		{
			Environment.SetEnvironmentVariable(FriendsServer.HostPcVariable, previous);
		}
	}

	private static byte[] ServerList(params (string Name, string Ip)[] servers)
	{
		using MemoryStream s = new MemoryStream();
		void Str(string v) { byte[] b = Encoding.UTF8.GetBytes(v); s.WriteByte((byte)(b.Length >> 8)); s.WriteByte((byte)b.Length); s.Write(b); }
		s.WriteByte(10); Str("");
		s.WriteByte(9); Str("servers"); s.WriteByte(10);
		s.WriteByte(0); s.WriteByte(0); s.WriteByte(0); s.WriteByte((byte)servers.Length);
		foreach ((string name, string ip) in servers)
		{
			s.WriteByte(8); Str("name"); Str(name);
			s.WriteByte(8); Str("ip"); Str(ip);
			s.WriteByte(0);
		}
		s.WriteByte(0);
		return s.ToArray();
	}

	[Fact]
	public void StaleServerListPortIsRefreshedFromSrv()
	{
		string path = Path.Combine(Path.GetTempPath(), "norcom-srvdat-" + Guid.NewGuid().ToString("n") + ".dat");
		try
		{
			File.WriteAllBytes(path, ServerList(("NORCOM", "shrimp-cub.tun.ply.gg:60313"), ("Other", "play.example.net:25565")));
			bool changed = ServersDat.RefreshListedPort(path, new FriendsEndpoint("shrimp-cub.tun.ply.gg", 25344, "srv"));
			Assert.True(changed);
			Assert.Equal(ServerList(("NORCOM", "shrimp-cub.tun.ply.gg:25344"), ("Other", "play.example.net:25565")), File.ReadAllBytes(path));
			Assert.False(ServersDat.RefreshListedPort(path, new FriendsEndpoint("shrimp-cub.tun.ply.gg", 25344, "srv")));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void ServerListIsNotRewrittenWithoutALiveSrvAnswer()
	{
		string path = Path.Combine(Path.GetTempPath(), "norcom-srvdat-" + Guid.NewGuid().ToString("n") + ".dat");
		try
		{
			byte[] original = ServerList(("NORCOM", "shrimp-cub.tun.ply.gg:60313"));
			File.WriteAllBytes(path, original);
			Assert.False(ServersDat.RefreshListedPort(path, new FriendsEndpoint("shrimp-cub.tun.ply.gg", 25344, "fallback")));
			Assert.False(ServersDat.RefreshListedPort(path, null));
			Assert.Equal(original, File.ReadAllBytes(path));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void Running270AcceptsPayloadAndExe290()
	{
		string sha = new string('a', 64);
		Assert.Null(UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.29", null, sha));
		Assert.Null(UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.29.0", null, sha));
		Assert.Null(UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.29+53e32ab", null, sha));
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.1", null, sha));
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.1+53e32ab", null, sha));
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.27", "2.0.29", "2.0.29", "2.0.27", null, sha));
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.27", "2.0.28", "2.0.28", "2.0.1", null, sha));
		Assert.True(UpdateReport.SameRelease("2.0.29", "2.0.29.0"));
	}
}
