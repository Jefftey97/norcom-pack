using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Tests;

public class PinGateTests
{
	private const string Pack = """
		version = "1.0.35"
		[versions]
		minecraft = "26.3"
		fabric = "0.20.1"
		""";

	private const string LivePin = "07983921b11ec8e8443bb307817c7b10c90c4388491c45d05da2a819108936ac";

	[Fact]
	public void FriendsFetchSlimPinThenCompat()
	{
		IReadOnlyList<string> urls = PinGate.CandidateUrls(null, null);
		Assert.Equal(PinGate.FriendsPinUrl, urls[0]);
		Assert.Equal(PinGate.ServerPinUrl, urls[1]);
		Assert.Equal(PinGate.CompatPinUrl, urls[2]);
	}

	[Fact]
	public async Task SlimPinMismatchRefuses()
	{
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("26.3", "0.20.1", LivePin)));
		await PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None);
		Assert.NotNull(PinGate.LastWarning);
		Assert.Contains(LivePin, PinGate.LastWarning);
		Assert.Equal("old-pin", PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task MinecraftMismatchRefuses()
	{
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("1.21.1", "0.20.1", LivePin)));
		await PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None);
		Assert.NotNull(PinGate.LastWarning);
		Assert.Contains("1.21.1", PinGate.LastWarning);
	}

	[Fact]
	public async Task FirstMatchingPinIsRemembered()
	{
		string dir = NewDir();
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("26.3", "0.20.1", LivePin)));
		await PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None);
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(dir));
		Assert.Null(PinGate.LastWarning);
	}

	[Fact]
	public async Task CompatBothMapDriftRefusesAndClientJarsMayDiffer()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "fabric-api.jar"), Encoding.UTF8.GetBytes("drift"));
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "sodium-fabric.jar"), Encoding.UTF8.GetBytes("client"));
		string compat = JsonSerializer.Serialize(new
		{
			schema = 1,
			mc = "26.3",
			loader = "0.20.1",
			pin = LivePin,
			packVersion = "1.0.35",
			indexSha256 = new string('e', 64),
			bothCount = 2,
			both = new Dictionary<string, string>
			{
				["fabric-api.jar"] = new string('a', 64),
				["sodium-fabric.jar"] = new string('b', 64)
			}
		});
		using HttpClient http = new(new RouteHandler(new Dictionary<string, (HttpStatusCode, string)>
		{
			["server-pin.json"] = (HttpStatusCode.OK, Slim("26.3", "0.20.1", LivePin)),
			["server-compat.json"] = (HttpStatusCode.OK, compat)
		}));
		await PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), CancellationToken.None);
		Assert.NotNull(PinGate.LastWarning);
		Assert.Contains("fabric-api.jar", PinGate.LastWarning);
	}

	[Fact]
	public async Task MissingPinWarnsUnlessTheFlagIsOn()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using (HttpClient open = new(new StubHandler(HttpStatusCode.NotFound, "")))
		{
			await PinGate.EnforceAsync(open, NewDir(), pack, PinGate.ServerPinUrl, CancellationToken.None);
			Assert.Equal(PinGate.MissingCopy, PinGate.LastWarning);
		}
		string previous = Environment.GetEnvironmentVariable(PinGate.RequireVariable);
		Environment.SetEnvironmentVariable(PinGate.RequireVariable, "1");
		try
		{
			using HttpClient closed = new(new StubHandler(HttpStatusCode.NotFound, ""));
			await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(closed, NewDir(), pack, PinGate.ServerPinUrl, CancellationToken.None));
		}
		finally
		{
			Environment.SetEnvironmentVariable(PinGate.RequireVariable, previous);
		}
	}

	private static string Slim(string mc, string loader, string pin)
	{
		return JsonSerializer.Serialize(new { schema = 1, mc, loader, pin, source = "server", updatedUtc = "2026-11-06T02:04:49Z" });
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-pin-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private sealed class RouteHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, (HttpStatusCode Code, string Body)> _routes;

		public RouteHandler(Dictionary<string, (HttpStatusCode, string)> routes)
		{
			_routes = routes;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string name = request.RequestUri?.Segments.Last() ?? "";
			if (!_routes.TryGetValue(name, out var route))
			{
				route = (HttpStatusCode.NotFound, "");
			}
			return Task.FromResult(new HttpResponseMessage(route.Code)
			{
				Content = new StringContent(route.Body, Encoding.UTF8, "application/json")
			});
		}
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _code;
		private readonly string _body;

		public StubHandler(HttpStatusCode code, string body)
		{
			_code = code;
			_body = body;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(_code)
			{
				Content = new StringContent(_body, Encoding.UTF8, "application/json")
			});
		}
	}
}
