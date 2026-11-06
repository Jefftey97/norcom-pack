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
		[index]
		file = "index.toml"
		hash-format = "sha256"
		hash = "abc123"
		[versions]
		minecraft = "26.3"
		fabric = "0.20.1"
		""";

	private const string LivePin = "07983921b11ec8e8443bb307817c7b10c90c4388491c45d05da2a819108936ac";

	[Fact]
	public void FriendsFetchServerPinThenCompat()
	{
		IReadOnlyList<string> urls = PinGate.CandidateUrls("https://example.test/pack/pack.toml", null);
		Assert.Equal(PinGate.ServerPinUrl, urls[0]);
		Assert.Equal(PinGate.CompatPinUrl, urls[1]);
		Assert.DoesNotContain(urls, url => url.Contains("norcom-mod-pin.json", StringComparison.Ordinal));
	}

	[Fact]
	public async Task CompatFallbackIsUsedWhenServerPinIsMissing()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = LockedPin("26.3", "0.20.1", LivePin);
		using HttpClient http = new(new RouteHandler(new Dictionary<string, (HttpStatusCode, string)>
		{
			["server-pin.json"] = (HttpStatusCode.NotFound, ""),
			["server-compat.json"] = (HttpStatusCode.OK, json)
		}));
		string dir = NewDir();
		await PinGate.EnforceAsync(http, dir, pack, CancellationToken.None);
		Assert.Null(PinGate.LastWarning);
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task ExtraFieldsAreIgnored()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = JsonSerializer.Serialize(new
		{
			schema = 1,
			mc = "26.3",
			loader = "0.20.1",
			pin = LivePin,
			source = "server",
			updatedUtc = "2026-11-06T02:04:49Z",
			packVersion = "9.9.9",
			indexSha256 = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
			both = new Dictionary<string, string> { ["Clumps.jar"] = new string('a', 64) }
		});
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, json));
		await PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/server-compat.json", CancellationToken.None);
		Assert.Null(PinGate.LastWarning);
	}

	[Fact]
	public async Task StoredPinMismatchRefuses()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, LockedPin("26.3", "0.20.1", LivePin)));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, dir, pack, "https://example.test/server-pin.json", CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains(LivePin, ex.Message);
		Assert.Contains("old-pin", ex.Message);
		Assert.Equal("old-pin", PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task MismatchedMinecraftBlocks()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, LockedPin("1.21.1", "0.20.1", LivePin)));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/server-pin.json", CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains("1.21.1", ex.Message);
	}

	[Fact]
	public async Task MissingPinWarnsAndContinues()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using HttpClient http = new(new StubHandler(HttpStatusCode.NotFound, ""));
		await PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/server-pin.json", CancellationToken.None);
		Assert.Equal(PinGate.MissingCopy, PinGate.LastWarning);
	}

	[Fact]
	public async Task MissingPinRefusesWhenTheFlagIsOn()
	{
		string previous = Environment.GetEnvironmentVariable(PinGate.RequireVariable);
		Environment.SetEnvironmentVariable(PinGate.RequireVariable, "1");
		try
		{
			TomlLite.Doc pack = TomlLite.Parse(Pack);
			using HttpClient http = new(new StubHandler(HttpStatusCode.NotFound, ""));
			PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/server-pin.json", CancellationToken.None));
			Assert.Equal(PinGate.BlockCopy, ex.Message);
		}
		finally
		{
			Environment.SetEnvironmentVariable(PinGate.RequireVariable, previous);
		}
	}

	private static string LockedPin(string mc, string loader, string pin)
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
