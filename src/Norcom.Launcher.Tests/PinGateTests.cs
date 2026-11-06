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

	[Fact]
	public void FriendsFetchTheModPinFile()
	{
		IReadOnlyList<string> urls = PinGate.CandidateUrls(null, null);
		Assert.Equal(PinGate.FriendsPinUrl, urls[0]);
	}

	[Fact]
	public void RollupMatchesThePublishedShape()
	{
		string pin = PinGate.Rollup(new[] { ("b.jar", "bb", 2L), ("a.jar", "aa", 1L) });
		Assert.Equal(64, pin.Length);
		Assert.Equal(pin, PinGate.Rollup(new[] { ("a.jar", "aa", 1L), ("b.jar", "bb", 2L) }));
	}

	[Fact]
	public async Task MinecraftAndLoaderMustMatch()
	{
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Document(("fabric-api.jar", "abc", 4))));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack.Replace("26.3", "1.21.1")), "https://example.test/norcom-mod-pin.json", CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains("pin_sha256", ex.Message);
	}

	[Fact]
	public async Task DualSidedJarDriftRefuses()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		byte[] body = Encoding.UTF8.GetBytes("not-the-pinned-bytes");
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "fabric-api.jar"), body);
		string wrong = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Document(("fabric-api.jar", new string('a', 64), 4))));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), "https://example.test/norcom-mod-pin.json", CancellationToken.None));
		Assert.StartsWith(PinGate.BlockCopy, ex.Message);
		Assert.Contains("fabric-api.jar", ex.Message);
		Assert.NotEqual(new string('a', 64), wrong);
	}

	[Fact]
	public async Task ClientOnlyJarsMayDiffer()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "sodium-fabric.jar"), new byte[] { 1, 2, 3 });
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "iris.jar"), new byte[] { 4 });
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "inventoryprofilesnext.jar"), new byte[] { 5 });
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "roughlyenoughitems.jar"), new byte[] { 6 });
		string json = Document(
			("sodium-fabric.jar", new string('b', 64), 3),
			("iris.jar", new string('c', 64), 1),
			("inventoryprofilesnext.jar", new string('d', 64), 1),
			("roughlyenoughitems.jar", new string('e', 64), 1));
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, json));
		await PinGate.EnforceAsync(http, dir, TomlLite.Parse(Pack), "https://example.test/norcom-mod-pin.json", CancellationToken.None);
		Assert.Null(PinGate.LastWarning);
	}

	[Fact]
	public async Task MissingServerJarDoesNotBlockFriends()
	{
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, Document(("alexsmobs.jar", new string('a', 64), 9))));
		await PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack), "https://example.test/norcom-mod-pin.json", CancellationToken.None);
		Assert.Null(PinGate.LastWarning);
	}

	[Fact]
	public async Task TamperedRollupRefuses()
	{
		string json = Document(("fabric-api.jar", new string('a', 64), 4)).Replace("\"pin_sha256\":", "\"pin_sha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"ignored\":");
		using HttpClient http = new(new StubHandler(HttpStatusCode.OK, json));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(http, NewDir(), TomlLite.Parse(Pack), "https://example.test/norcom-mod-pin.json", CancellationToken.None));
		Assert.Contains("rollup", ex.Message);
	}

	[Fact]
	public async Task MissingPinWarnsUnlessTheFlagIsOn()
	{
		TomlLite.Doc pack = TomlLite.Parse(Pack);
		using HttpClient open = new(new StubHandler(HttpStatusCode.NotFound, ""));
		await PinGate.EnforceAsync(open, NewDir(), pack, "https://example.test/norcom-mod-pin.json", CancellationToken.None);
		Assert.Equal(PinGate.MissingCopy, PinGate.LastWarning);
		string previous = Environment.GetEnvironmentVariable(PinGate.RequireVariable);
		Environment.SetEnvironmentVariable(PinGate.RequireVariable, "1");
		try
		{
			using HttpClient closed = new(new StubHandler(HttpStatusCode.NotFound, ""));
			await Assert.ThrowsAsync<PinMismatchException>(() => PinGate.EnforceAsync(closed, NewDir(), pack, "https://example.test/norcom-mod-pin.json", CancellationToken.None));
		}
		finally
		{
			Environment.SetEnvironmentVariable(PinGate.RequireVariable, previous);
		}
	}

	private static string Document(params (string Name, string Sha, long Size)[] jars)
	{
		string pin = PinGate.Rollup(jars.Select(jar => (jar.Name, jar.Sha, jar.Size)));
		return JsonSerializer.Serialize(new
		{
			schema = 1,
			minecraft = "26.3",
			fabric_loader = "0.20.1",
			source = "G:\\MinecraftServer\\mods",
			jar_count = jars.Length,
			jars = jars.Select(jar => new { name = jar.Name, sha256 = jar.Sha, size = jar.Size }).ToArray(),
			pin_sha256 = pin
		});
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-pin-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
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
