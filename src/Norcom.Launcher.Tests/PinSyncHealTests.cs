using System.Net;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PinSyncHealTests
{
	private const string LivePin = "0517b6b64a967471914d12f7567823ac8a329e7d7e2200575b841350fc3afc48";

	private const string LiveMarble = "3566420dd6cfc2a104c4b7b17bfeebedb20551d2f32b1502fe03748ca9728c48";

	private const string JarUrl = "https://pins.test/mods/norcom-marble-1.0.0.jar";

	[Fact]
	public void BakedPinIsTheLivePublishedPin()
	{
		Assert.Equal(LivePin, PinGate.LivePin);
		Assert.Equal(LiveMarble, PinGate.LiveMarbleSha);
		Assert.Equal("norcom-marble-1.0.0.jar", PinGate.MarbleJarName);
		Assert.True(PinGate.IsTemporaryPin("TEMP"));
		Assert.True(PinGate.IsTemporaryPin("TEMP-bridge"));
		Assert.True(PinGate.IsTemporaryPin("temp_old"));
		Assert.False(PinGate.IsTemporaryPin(LivePin));
	}

	[Fact]
	public async Task MissingMarbleIsReplacedAndBothStampsRecordTheLivePin()
	{
		PinFetch.ResetForTests();
		byte[] live = Encoding.UTF8.GetBytes("live-marble-bytes");
		string sha = Hex(live);
		(string dir, string primary) roots = Roots();
		using HttpClient http = new(new HealHandler(PinJson(LivePin, sha, live.Length, JarUrl), live));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: true, roots.primary);
		Assert.Equal("correct", result.State);
		Assert.Equal(LivePin, result.Pin);
		Assert.Equal(sha, Hex(await File.ReadAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName))));
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(roots.dir));
		Assert.Equal(LivePin, PinGate.ReadPrimaryAppliedPin(roots.primary));
		Assert.Equal(LivePin, PinGate.ReadPrimaryAppliedPin(roots.primary));
	}

	[Fact]
	public async Task OldMarbleIsReplacedWithThePublishedJar()
	{
		PinFetch.ResetForTests();
		byte[] live = Encoding.UTF8.GetBytes("published-marble-3566420d");
		byte[] old = Encoding.UTF8.GetBytes("baked-marble-47fe426d");
		string sha = Hex(live);
		Assert.NotEqual(LiveMarble, sha);
		Assert.NotEqual(Hex(old), sha);
		(string dir, string primary) roots = Roots();
		Directory.CreateDirectory(Path.Combine(roots.dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName), old);
		await File.WriteAllTextAsync(Path.Combine(roots.dir, PinGate.AppliedPinFile), "49ff8e68f03f40d4eacdb98ad9848d17f977ef1bf2cc1f08a1e8b216c70bb9d1\n");
		using HttpClient http = new(new HealHandler(PinJson(LivePin, sha, live.Length, JarUrl), live));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: false, roots.primary);
		Assert.Equal("correct", result.State);
		Assert.Equal(sha, Hex(await File.ReadAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName))));
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(roots.dir));
		Assert.Equal(LivePin, PinGate.ReadPrimaryAppliedPin(roots.primary));
	}

	[Fact]
	public async Task FailedRepairLeavesTheAppliedPinUnchanged()
	{
		PinFetch.ResetForTests();
		byte[] old = Encoding.UTF8.GetBytes("old-marble");
		byte[] bogus = Encoding.UTF8.GetBytes("not-live-marble");
		(string dir, string primary) roots = Roots();
		Directory.CreateDirectory(Path.Combine(roots.dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName), old);
		await File.WriteAllTextAsync(Path.Combine(roots.dir, PinGate.AppliedPinFile), "keep-applied\n");
		string pinJson = PinJson(LivePin, LiveMarble, bogus.Length, JarUrl);
		using HttpClient http = new(new HealHandler(pinJson, bogus));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: true, roots.primary);
		Assert.Equal("mismatch", result.State);
		Assert.Equal("keep-applied", result.Pin);
		Assert.Equal("keep-applied", PinGate.ReadAppliedPin(roots.dir));
		Assert.Null(PinGate.ReadPrimaryAppliedPin(roots.primary));
		Assert.Equal("old-marble", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName))));
		Assert.Equal("a pack jar does not match the published pin", result.Detail);
	}

	[Fact]
	public async Task TemporaryPinIsRefusedAndStampsStay()
	{
		PinFetch.ResetForTests();
		(string dir, string primary) roots = Roots();
		await File.WriteAllTextAsync(Path.Combine(roots.dir, PinGate.AppliedPinFile), "keep-applied\n");
		string pinJson = PinJson("TEMP-bridge", LiveMarble, 1, JarUrl);
		using HttpClient http = new(new HealHandler(pinJson, new byte[] { 1 }));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: true, roots.primary);
		Assert.Equal("mismatch", result.State);
		Assert.Equal("published pin is temporary", result.Detail);
		Assert.Equal("keep-applied", PinGate.ReadAppliedPin(roots.dir));
		Assert.Null(PinGate.ReadPrimaryAppliedPin(roots.primary));
		Assert.False(File.Exists(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName)));
	}

	[Fact]
	public async Task MatchingJarsWriteThePrimaryPinWithoutADownload()
	{
		PinFetch.ResetForTests();
		byte[] live = Encoding.UTF8.GetBytes("already-live-marble");
		string sha = Hex(live);
		(string dir, string primary) roots = Roots();
		Directory.CreateDirectory(Path.Combine(roots.dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName), live);
		await File.WriteAllTextAsync(Path.Combine(roots.dir, PinGate.AppliedPinFile), LivePin + "\n");
		HealHandler handler = new HealHandler(PinJson(LivePin, sha, live.Length, JarUrl), live);
		using HttpClient http = new(handler);
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: false, roots.primary);
		Assert.Equal("correct", result.State);
		Assert.Equal("published pin", result.Detail);
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(roots.dir));
		Assert.Equal(LivePin, PinGate.ReadPrimaryAppliedPin(roots.primary));
		Assert.DoesNotContain(handler.Urls, url => url == JarUrl);
		Assert.Equal(sha, Hex(await File.ReadAllBytesAsync(Path.Combine(roots.dir, "mods", PinGate.MarbleJarName))));
	}

	[Fact]
	public async Task OpenPollHealsALaggingJarAndFixUsesTheSamePath()
	{
		PinFetch.ResetForTests();
		byte[] live = Encoding.UTF8.GetBytes("fabric-live");
		byte[] old = Encoding.UTF8.GetBytes("fabric-old");
		string sha = Hex(live);
		string url = "https://pins.test/mods/fabric-api.jar";
		(string dir, string primary) roots = Roots();
		Directory.CreateDirectory(Path.Combine(roots.dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(roots.dir, "mods", "fabric-api.jar"), old);
		string pinJson = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + LivePin + "\",\"jars\":[{\"name\":\"fabric-api.jar\",\"sha256\":\"" + sha + "\",\"size\":" + live.Length + ",\"url\":\"" + url + "\"},{\"name\":\"server-only.jar\",\"sha256\":\"" + new string('a', 64) + "\",\"size\":4}]}";
		using HttpClient http = new(new HealHandler(pinJson, live, url));
		PinGate.PinSyncResult open = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: false, roots.primary);
		Assert.Equal("correct", open.State);
		Assert.Equal(sha, Hex(await File.ReadAllBytesAsync(Path.Combine(roots.dir, "mods", "fabric-api.jar"))));
		Assert.False(File.Exists(Path.Combine(roots.dir, "mods", "server-only.jar")));
		Assert.Equal(LivePin, PinGate.ReadPrimaryAppliedPin(roots.primary));
		PinGate.PinSyncResult fix = await PinGate.SyncPublishedAsync(http, roots.dir, CancellationToken.None, forceTip: true, roots.primary);
		Assert.Equal("correct", fix.State);
		Assert.Equal(LivePin, PinGate.ReadAppliedPin(roots.dir));
	}

	private static (string dir, string primary) Roots()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-sync-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return (dir, Path.Combine(dir, "primary"));
	}

	private static string PinJson(string pin, string sha, int size, string url)
	{
		return "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":[{\"name\":\"" + PinGate.MarbleJarName + "\",\"sha256\":\"" + sha + "\",\"size\":" + size + ",\"url\":\"" + url + "\"}]}";
	}

	private static string Hex(byte[] bytes)
	{
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	private sealed class HealHandler : HttpMessageHandler
	{
		private readonly string _pin;
		private readonly byte[] _jar;
		private readonly string _jarUrl;

		public HealHandler(string pin, byte[] jar, string? jarUrl = null)
		{
			_pin = pin;
			_jar = jar;
			_jarUrl = jarUrl ?? JarUrl;
		}

		public List<string> Urls { get; } = new List<string>();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			Urls.Add(url);
			if (url.Contains("/commits/main", StringComparison.Ordinal))
			{
				return Task.FromResult(Json("{\"sha\":\"" + PinFetch.KnownTip + "\"}"));
			}
			if (url == _jarUrl)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(_jar)
				});
			}
			if (url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
			{
				return Task.FromResult(Json(_pin));
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		private static HttpResponseMessage Json(string body)
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			};
		}
	}
}
