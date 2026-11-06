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

	[Fact]
	public async Task FallbackPinIsUsedWhenPrimaryIsMissing()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = JsonSerializer.Serialize(new { schema = 1, mc = "26.3", loader = "0.20.1", pin = "abc123", source = "server" });
		using HttpClient http = new(new RouteHandler(new Dictionary<string, (HttpStatusCode, string)>
		{
			["norcom-mod-pin.json"] = (HttpStatusCode.NotFound, ""),
			["server-pin.json"] = (HttpStatusCode.NotFound, ""),
			["server-compat.json"] = (HttpStatusCode.OK, json)
		}));
		await PinGate.EnforceAsync(http, NewDir(), pack, CancellationToken.None);
		Assert.Null(PinGate.LastWarning);
	}

	[Fact]
	public async Task PresentPinMismatchRefuses()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = JsonSerializer.Serialize(new { schema = 1, mc = "26.3", loader = "0.20.1", pin = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", source = "server" });
		using HttpClient http = new(new RouteHandler(new Dictionary<string, (HttpStatusCode, string)>
		{
			["norcom-mod-pin.json"] = (HttpStatusCode.OK, json)
		}));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), pack, CancellationToken.None));
		Assert.Equal(PinGate.BlockCopy, ex.Message);
	}

	[Fact]
	public async Task MissingPinWarnsAndContinues()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using HttpClient http = new(new StubHandler(HttpStatusCode.NotFound, ""));
		await PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/pin.json", CancellationToken.None);
		Assert.Equal(PinGate.MissingCopy, PinGate.LastWarning);
	}

	[Fact]
	public async Task MismatchedMinecraftBlocks()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = JsonSerializer.Serialize(new { schema = 1, mc = "1.21.1", loader = "0.20.1", packVersion = "1.0.35", indexSha256 = "abc123", source = "server" });
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, json));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), pack, "https://example.test/pin.json", CancellationToken.None));
		Assert.Equal(PinGate.BlockCopy, ex.Message);
	}

	[Fact]
	public async Task ClientModsAreIgnoredAndBothJarMustMatch()
	{
		string dir = NewDir();
		string jar = Path.Combine(dir, "mods", "fabric-api.jar");
		Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
		await File.WriteAllBytesAsync(jar, new byte[] { 1, 2, 3, 4 });
		string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 })).ToLowerInvariant();
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		string json = JsonSerializer.Serialize(new
		{
			schema = 1,
			mc = "26.3",
			loader = "0.20.1",
			packVersion = "1.0.35",
			indexSha256 = "abc123",
			both = new Dictionary<string, object>
			{
				["sodium"] = new { jar = "mods/sodium.jar", hash = "deadbeef" },
				["fabric-api"] = new { jar = "mods/fabric-api.jar", hash }
			},
			source = "server"
		});
		JsonElement pin = JsonDocument.Parse(json).RootElement;
		Assert.Null(await PinGate.DescribeMismatchAsync(pin, pack, dir, CancellationToken.None));
		json = json.Replace(hash, new string('a', 64));
		pin = JsonDocument.Parse(json).RootElement;
		Assert.Contains("fabric-api", await PinGate.DescribeMismatchAsync(pin, pack, dir, CancellationToken.None));
	}

	[Theory]
	[InlineData("sodium", "mods/sodium.jar")]
	[InlineData("iris", "mods/iris.jar")]
	[InlineData("rei", "mods/rei.jar")]
	[InlineData("some-pack", "resourcepacks/Patrix.zip")]
	[InlineData("shade", "shaderpacks/Complementary.zip")]
	public void ClientSurfacesAreIgnored(string modid, string jar)
	{
		Assert.True(PinGate.IsClientOnly(modid, jar));
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
