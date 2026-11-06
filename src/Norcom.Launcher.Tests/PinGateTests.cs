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
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains(LivePin, ex.Message);
		Assert.Equal("old-pin", PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task MinecraftMismatchRefuses()
	{
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("1.21.1", "0.20.1", LivePin)));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains("1.21.1", ex.Message);
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
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains("fabric-api.jar", ex.Message);
	}

	[Fact]
	public async Task WrongLivePinRefuses()
	{
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("26.3", "0.20.1", new string('a', 64))));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack), PinGate.ServerPinUrl, CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains(PinGate.LivePin, ex.Message);
	}

	[Fact]
	public async Task PresentPinJarsMatchEvenWhenTheFolderIsNotTheServerRollup()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		byte[] fabric = Encoding.UTF8.GetBytes("fabric-api-bytes");
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "fabric-api.jar"), fabric);
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "sodium-fabric.jar"), Encoding.UTF8.GetBytes("client-only"));
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "iris-fabric.jar"), Encoding.UTF8.GetBytes("client-only-2"));
		string fabricSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fabric)).ToLowerInvariant();
		string pinJson = FullPin(new[]
		{
			Jar("fabric-api.jar", fabricSha, fabric.Length),
			Jar("server-only-mod.jar", new string('a', 64), 10)
		});
		Assert.NotEqual(LivePin, PinGate.Rollup(new[]
		{
			("fabric-api.jar", fabricSha, (long)fabric.Length),
			("server-only-mod.jar", new string('a', 64), 10L)
		}));
		string? previous = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		string pinPath = Path.Combine(dir, "pin.json");
		await File.WriteAllTextAsync(pinPath, pinJson);
		try
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", pinPath);
			using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("26.3", "0.20.1", LivePin)));
			await PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), CancellationToken.None);
			Assert.Equal(LivePin, PinGate.ReadAppliedPin(dir));
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", previous);
		}
	}

	[Fact]
	public async Task DualSidedShaMismatchStillRefuses()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		byte[] fabric = Encoding.UTF8.GetBytes("wrong-bytes");
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "fabric-api.jar"), fabric);
		string pinJson = FullPin(new[]
		{
			Jar("fabric-api.jar", new string('b', 64), fabric.Length)
		});
		string? previous = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		string pinPath = Path.Combine(dir, "pin.json");
		await File.WriteAllTextAsync(pinPath, pinJson);
		try
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", pinPath);
			using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Slim("26.3", "0.20.1", LivePin)));
			PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), CancellationToken.None));
			Assert.StartsWith(PinGate.BlockCopy, ex.Message);
			Assert.Contains("fabric-api.jar", ex.Message);
			Assert.DoesNotContain("Actual rollup", ex.Message);
			Assert.DoesNotContain("Repair", ex.Message);
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", previous);
		}
	}

	[Fact]
	public async Task AllPinUrls404WarnAndContinue()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using HttpClient http = new(new RouteHandler(new Dictionary<string, (HttpStatusCode, string)>()));
		await PinGate.EnforceAsync(http, NewDir(), pack, CancellationToken.None);
		Assert.Equal(PinGate.MissingCopy, PinGate.LastWarning);
	}

	private static string Jar(string name, string sha, int size)
	{
		return "{\"name\":" + JsonSerializer.Serialize(name) + ",\"sha256\":" + JsonSerializer.Serialize(sha) + ",\"size\":" + size + "}";
	}

	private static string FullPin(IEnumerable<string> jars)
	{
		return "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + LivePin + "\",\"jars\":[" + string.Join(",", jars) + "]}";
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
