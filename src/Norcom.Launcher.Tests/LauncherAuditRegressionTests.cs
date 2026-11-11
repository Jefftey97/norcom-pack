using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

// Regressions for the 2026-11-10 launcher audit. All network traffic is
// served by in-memory handlers; files live in per-test temp folders.
public class LauncherAuditRegressionTests
{
	private const string NewerPin = "d3e28ef8030f985e6cac28981ed96a2d170baf300f50f52de87fa6020da61e63";
	private const string NewerGeneratedAt = "2026-11-06T14:25:17-07:00";

	[Fact]
	public async Task ArchivalCommitRefCannotRollAnAppliedNewerPinBackToTheBakedPin()
	{
		// Observed live 2026-11-09 19:22 MST: with the GitHub API unavailable the
		// hard-coded commit copy served the older baked pin and a jar heal toward it began.
		PinFetch.ResetForTests();
		(string dir, string primary) = Roots();
		byte[] current = Encoding.UTF8.GetBytes("jar-for-the-newer-pin");
		byte[] older = Encoding.UTF8.GetBytes("jar-for-the-baked-pin");
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "alexsmobs.jar"), current);
		Directory.CreateDirectory(primary);
		// Legacy stamp written by 3.0.1: no generated_at.
		string legacyStamp = "{\"schema\":1,\"pin\":\"" + PinGate.LivePin + "\",\"pin_sha256\":\"" + PinGate.LivePin + "\",\"appliedUtc\":\"2026-11-10T02:24:47Z\"}";
		await File.WriteAllTextAsync(Path.Combine(primary, PinGate.PrimaryAppliedPinFile), legacyStamp);
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
		string baked = Doc(PinGate.PreviousPin, "2026-11-06T13:04:44-07:00", "alexsmobs.jar", older, "https://pins.test/old/alexsmobs.jar");
		Handler handler = new Handler { ArchivalBody = baked, JarBytes = older };
		using HttpClient http = new(handler);

		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: false, primary);

		Assert.Equal("mismatch", result.State);
		Assert.Equal(PinGate.LivePin, result.Pin);
		Assert.Equal(current, await File.ReadAllBytesAsync(Path.Combine(dir, "mods", "alexsmobs.jar")));
		Assert.Equal(legacyStamp, await File.ReadAllTextAsync(Path.Combine(primary, PinGate.PrimaryAppliedPinFile)));
		Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(dir));
		Assert.DoesNotContain(handler.Urls, url => url.Contains("pins.test", StringComparison.Ordinal));
		// The archival copy was skipped and the fresh fallbacks were still asked.
		Assert.Contains(handler.Urls, url => url.Contains("cdn.jsdelivr.net", StringComparison.Ordinal));
		Assert.Contains(handler.Urls, url => url.Contains("?t=", StringComparison.Ordinal));
	}

	[Fact]
	public async Task FreshFallbackIsUsedAndRecordsItsPublicationTime()
	{
		PinFetch.ResetForTests();
		(string dir, string primary) = Roots();
		Directory.CreateDirectory(primary);
		await File.WriteAllTextAsync(Path.Combine(primary, PinGate.PrimaryAppliedPinFile), "{\"schema\":1,\"pin_sha256\":\"" + NewerPin + "\"}");
		string newest = new string('f', 64);
		Handler handler = new Handler
		{
			ArchivalBody = Doc(PinGate.LivePin, PinGate.LivePinGeneratedAt, null, null, null),
			BustedBody = Doc(newest, "2026-11-08T09:00:00-07:00", null, null, null)
		};
		using HttpClient http = new(handler);

		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: true, primary);

		Assert.Equal("correct", result.State);
		Assert.Equal(newest, PinGate.ReadPrimaryAppliedPin(primary));
		Assert.Equal("2026-11-08T09:00:00-07:00", PinGate.ReadPrimaryAppliedGeneratedAt(primary));
		Assert.Equal(newest, PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public void PinsMoveForwardOnlyUnlessTheOwnerRepublishesWithANewerTime()
	{
		(string dir, string primary) = Roots();
		Directory.CreateDirectory(primary);
		string future = new string('c', 64);
		File.WriteAllText(Path.Combine(primary, PinGate.PrimaryAppliedPinFile),
			"{\"schema\":1,\"pin_sha256\":\"" + future + "\",\"generated_at\":\"2026-11-10T12:00:00-07:00\"}");
		Assert.True(PinGate.IsOlderThanApplied(Doc(PinGate.LivePin, PinGate.LivePinGeneratedAt, null, null, null), primary));
		Assert.True(PinGate.IsOlderThanApplied(Doc(new string('a', 64), "2026-11-06T14:00:00-07:00", null, null, null), primary));
		// Deliberate owner rollback: an older pin, republished later than the applied stamp.
		Assert.False(PinGate.IsOlderThanApplied(Doc(PinGate.LivePin, "2026-11-12T12:00:00-07:00", null, null, null), primary));
		// Same pin, unknown time, malformed text and missing stamps never block.
		Assert.False(PinGate.IsOlderThanApplied(Doc(future, "2020-01-01T00:00:00Z", null, null, null), primary));
		Assert.False(PinGate.IsOlderThanApplied(Doc(new string('b', 64), null, null, null, null), primary));
		Assert.False(PinGate.IsOlderThanApplied("not json", primary));
		Assert.False(PinGate.IsOlderThanApplied(Doc(PinGate.LivePin, PinGate.LivePinGeneratedAt, null, null, null), Path.Combine(dir, "none")));
		// A PC still on the baked pin can move to anything not older than it.
		File.WriteAllText(Path.Combine(primary, PinGate.PrimaryAppliedPinFile), "{\"schema\":1,\"pin_sha256\":\"" + PinGate.LivePin + "\"}");
		Assert.False(PinGate.IsOlderThanApplied(Doc(future, "2026-11-10T12:00:00-07:00", null, null, null), primary));
	}

	[Fact]
	public async Task ContentsApiPollUsesConditionalRequestsAndReusesTheCachedPin()
	{
		PinFetch.ResetForTests();
		string body = Doc(NewerPin, NewerGeneratedAt, null, null, null);
		Handler handler = new Handler { ContentsBody = body, ContentsETag = "\"v1\"" };
		using HttpClient http = new(handler);
		string? first = await PinFetch.FetchNamedAsync(http, "norcom-mod-pin.json", CancellationToken.None, forceTip: false, Path.Combine(Path.GetTempPath(), "norcom-none-" + Guid.NewGuid().ToString("n")));
		string? second = await PinFetch.FetchNamedAsync(http, "norcom-mod-pin.json", CancellationToken.None, forceTip: false, Path.Combine(Path.GetTempPath(), "norcom-none-" + Guid.NewGuid().ToString("n")));
		Assert.Equal(body, first);
		Assert.Equal(body, second);
		Assert.Equal(1, handler.NotModifiedResponses);
		Assert.Equal(new[] { (string?)null, "\"v1\"" }, handler.ContentsConditions.ToArray());
		Assert.DoesNotContain(handler.Urls, url => url.Contains(PinFetch.KnownTip + "/meta", StringComparison.Ordinal));
	}

	[WindowsFact]
	public async Task FailedMultiJarHealRestoresEveryPreviousJar()
	{
		PinFetch.ResetForTests();
		(string dir, string primary) = Roots();
		string mods = Path.Combine(dir, "mods");
		Directory.CreateDirectory(mods);
		byte[] oldA = Encoding.UTF8.GetBytes("old-a"), oldB = Encoding.UTF8.GetBytes("old-b");
		byte[] newA = Encoding.UTF8.GetBytes("new-a!"), newB = Encoding.UTF8.GetBytes("new-b!");
		await File.WriteAllBytesAsync(Path.Combine(mods, "a.jar"), oldA);
		await File.WriteAllBytesAsync(Path.Combine(mods, "b.jar"), oldB);
		string pin = new string('9', 64);
		string doc = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":[" +
			Jar("a.jar", newA, "https://pins.test/a.jar") + "," + Jar("b.jar", newB, "https://pins.test/b.jar") + "]}";
		Handler handler = new Handler { ContentsBody = doc };
		handler.Files["https://pins.test/a.jar"] = newA;
		handler.Files["https://pins.test/b.jar"] = newB;
		using HttpClient http = new(handler);
		PinGate.PinSyncResult result;
		// Like a running game: b.jar stays readable but cannot be moved or replaced.
		using (new FileStream(Path.Combine(mods, "b.jar"), FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: false, primary);
		}
		Assert.Equal("mismatch", result.State);
		Assert.Equal(oldA, await File.ReadAllBytesAsync(Path.Combine(mods, "a.jar")));
		Assert.Equal(oldB, await File.ReadAllBytesAsync(Path.Combine(mods, "b.jar")));
		Assert.Equal(new[] { "a.jar", "b.jar" }, Directory.GetFiles(mods).Select(Path.GetFileName).OrderBy(x => x).ToArray());
		Assert.Null(PinGate.ReadPrimaryAppliedPin(primary));

		PinGate.PinSyncResult healed = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: false, primary);
		Assert.Equal("correct", healed.State);
		Assert.Equal(newA, await File.ReadAllBytesAsync(Path.Combine(mods, "a.jar")));
		Assert.Equal(newB, await File.ReadAllBytesAsync(Path.Combine(mods, "b.jar")));
		Assert.Equal(new[] { "a.jar", "b.jar" }, Directory.GetFiles(mods).Select(Path.GetFileName).OrderBy(x => x).ToArray());
	}

	[Fact]
	public async Task OversizedJarResponseIsRefusedWithoutReplacingTheJar()
	{
		PinFetch.ResetForTests();
		(string dir, string primary) = Roots();
		string mods = Path.Combine(dir, "mods");
		Directory.CreateDirectory(mods);
		byte[] old = Encoding.UTF8.GetBytes("old");
		byte[] expected = Encoding.UTF8.GetBytes("expected");
		await File.WriteAllBytesAsync(Path.Combine(mods, "a.jar"), old);
		string doc = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + new string('8', 64) + "\",\"jars\":[" + Jar("a.jar", expected, "https://pins.test/a.jar") + "]}";
		Handler handler = new Handler { ContentsBody = doc };
		handler.Files["https://pins.test/a.jar"] = new byte[1024 * 1024];
		using HttpClient http = new(handler);
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: false, primary);
		Assert.Equal("mismatch", result.State);
		Assert.Equal(old, await File.ReadAllBytesAsync(Path.Combine(mods, "a.jar")));
	}

	[Fact]
	public void FailedStagingLeavesNoStagingFolderAndKeepsInstalledPayloads()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-stage-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "NorcomLauncher.exe"), "bootstrap; never execute");
		using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		ReleaseVerifier verifier = new(new Dictionary<string, string> { { "test-only", key.ExportSubjectPublicKeyInfoPem() } });
		string zip = Path.Combine(root, "pkg.zip");
		byte[] exe = Encoding.UTF8.GetBytes("candidate");
		using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
		{
			using (Stream output = archive.CreateEntry("NorcomLauncher.exe").Open()) output.Write(exe);
			using (StreamWriter writer = new StreamWriter(archive.CreateEntry("payload-version.txt").Open())) writer.Write("3.0.2");
		}
		DateTimeOffset now = DateTimeOffset.UtcNow;
		// The signed executable hash deliberately disagrees with the archive bytes.
		ReleasePayload payload = new(1, ReleaseVerifier.Product, ReleaseVerifier.Channel, "test-only", 1, now.AddMinutes(-1), now.AddDays(1), "3.0.2", 1,
			"https://raw.githubusercontent.com/Jefftey97/norcom-pack/" + new string('a', 40) + "/launcher/NorcomWpf-3.0.2.zip",
			new FileInfo(zip).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),
			exe.Length, new string('0', 64));
		VerifiedRelease release = verifier.Verify(SignedReleaseTests.Sign(key, JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })), now);
		UpdateStore store = new(root, verifier);
		using (store.Acquire())
		{
			Assert.Throws<InvalidDataException>(() => store.Stage(release, zip));
		}
		Assert.Empty(Directory.GetDirectories(Path.Combine(root, "update"), "stage-*"));
		Assert.False(Directory.Exists(Path.Combine(root, "app")));
		Assert.Equal("bootstrap; never execute", File.ReadAllText(Path.Combine(root, "NorcomLauncher.exe")));
	}

	private static (string dir, string primary) Roots()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-audit-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return (dir, Path.Combine(dir, "primary"));
	}

	private static string Jar(string name, byte[] bytes, string url)
	{
		return "{\"name\":\"" + name + "\",\"sha256\":\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\",\"size\":" + bytes.Length + ",\"url\":\"" + url + "\"}";
	}

	private static string Doc(string pin, string? generatedAt, string? jarName, byte[]? jar, string? url)
	{
		string time = generatedAt == null ? "" : "\"generated_at\":\"" + generatedAt + "\",";
		string jars = jarName == null ? "[]" : "[" + Jar(jarName, jar!, url!) + "]";
		return "{\"schema\":1," + time + "\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":" + jars + "}";
	}

	private sealed class Handler : HttpMessageHandler
	{
		public string? ContentsBody { get; init; }
		public string? ContentsETag { get; init; }
		public string? ArchivalBody { get; init; }
		public string? BustedBody { get; init; }
		public byte[]? JarBytes { get; init; }
		public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		public List<string> Urls { get; } = new List<string>();
		public List<string?> ContentsConditions { get; } = new List<string?>();
		public int NotModifiedResponses { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			Urls.Add(url);
			if (Files.TryGetValue(url, out byte[]? file))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
			}
			if (url.Contains("pins.test", StringComparison.Ordinal) && JarBytes != null)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(JarBytes) });
			}
			if (url.Contains("/commits/main", StringComparison.Ordinal))
			{
				// API unavailable (rate limited) unless the test serves contents.
				return Task.FromResult(new HttpResponseMessage(ContentsBody == null ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound));
			}
			if (url.Contains("/contents/meta/norcom-mod-pin.json", StringComparison.Ordinal))
			{
				if (ContentsBody == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
				string? condition = request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
				ContentsConditions.Add(condition);
				if (ContentsETag != null && condition == ContentsETag)
				{
					NotModifiedResponses++;
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
				}
				string wrapped = "{\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(ContentsBody)) + "\"}";
				HttpResponseMessage ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(wrapped, Encoding.UTF8, "application/json") };
				if (ContentsETag != null) ok.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ContentsETag);
				return Task.FromResult(ok);
			}
			if (url.Contains(PinFetch.KnownTip + "/meta/norcom-mod-pin.json", StringComparison.Ordinal) && ArchivalBody != null)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ArchivalBody, Encoding.UTF8, "application/json") });
			}
			if (url.Contains("/main/meta/norcom-mod-pin.json?t=", StringComparison.Ordinal) && BustedBody != null)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BustedBody, Encoding.UTF8, "application/json") });
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}
}
