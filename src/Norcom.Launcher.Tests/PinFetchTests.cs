using System.Net;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PinFetchTests
{
	private const string Published = "49ff8e68f03f40d4eacdb98ad9848d17f977ef1bf2cc1f08a1e8b216c70bb9d1";

	[Fact]
	public void SourceOrderIsCommitRefThenJsDelivrThenContentsThenBustedRaw()
	{
		PinFetch.ResetForTests();
		IReadOnlyList<PinSource> sources = PinFetch.Sources("norcom-mod-pin.json", PinFetch.KnownTip, "42");
		Assert.Equal(4, sources.Count);
		Assert.Equal("commit-ref", sources[0].Kind);
		Assert.Contains("/" + PinFetch.KnownTip + "/meta/norcom-mod-pin.json", sources[0].Url);
		Assert.Equal("jsdelivr", sources[1].Kind);
		Assert.Contains("cdn.jsdelivr.net/gh/Jefftey97/norcom-pack@main/meta/norcom-mod-pin.json", sources[1].Url);
		Assert.Equal("contents-api", sources[2].Kind);
		Assert.Contains("api.github.com/repos/Jefftey97/norcom-pack/contents/meta/norcom-mod-pin.json?ref=main", sources[2].Url);
		Assert.Equal("raw-busted", sources[3].Kind);
		Assert.Contains("?t=42", sources[3].Url);
		Assert.DoesNotContain(sources, source => source.Url == PinFetch.BareRawMain("norcom-mod-pin.json"));
	}

	[Fact]
	public async Task OpenPollStopsAtCommitRefAndDoesNotRequestBareRawMain()
	{
		PinFetch.ResetForTests();
		List<string> urls = new List<string>();
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		using HttpClient http = new(new OrderHandler(urls, commitRefBody: Full(Published, null)));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: false);
		Assert.Equal("correct", result.State);
		Assert.Equal(Published, result.Pin);
		Assert.Equal(Published, PinGate.ReadAppliedPin(dir));
		Assert.Contains(urls, url => url.Contains("/" + PinFetch.KnownTip + "/meta/norcom-mod-pin.json"));
		Assert.DoesNotContain(urls, url => url.Contains("cdn.jsdelivr.net"));
		Assert.DoesNotContain(urls, url => url == PinFetch.BareRawMain("norcom-mod-pin.json"));
	}

	[Fact]
	public async Task FixFallsThroughJsDelivrThenContentsAndStillSkipsBareRaw()
	{
		PinFetch.ResetForTests();
		List<string> urls = new List<string>();
		string pinJson = Full(Published, null);
		string wrapped = JsonSerializer.Serialize(new
		{
			encoding = "base64",
			content = Convert.ToBase64String(Encoding.UTF8.GetBytes(pinJson))
		});
		string dir = NewDir();
		using HttpClient http = new(new OrderHandler(urls, commitRefBody: null, jsDelivrBody: null, contentsBody: wrapped));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: true);
		Assert.Equal("correct", result.State);
		Assert.Equal(Published, PinGate.ReadAppliedPin(dir));
		int commit = urls.FindIndex(url => url.Contains("/" + PinFetch.KnownTip + "/meta/norcom-mod-pin.json"));
		int js = urls.FindIndex(url => url.Contains("cdn.jsdelivr.net/gh/Jefftey97/norcom-pack@main/meta/norcom-mod-pin.json"));
		int api = urls.FindIndex(url => url.Contains("api.github.com/repos/Jefftey97/norcom-pack/contents/meta/norcom-mod-pin.json"));
		Assert.True(commit >= 0 && js > commit && api > js);
		Assert.DoesNotContain(urls, url => url.Contains("/main/meta/norcom-mod-pin.json?t="));
		Assert.DoesNotContain(urls, url => url == PinFetch.BareRawMain("norcom-mod-pin.json"));
	}

	[Fact]
	public async Task JarDriftUsesTheFetchedPinNotTheBakedConstant()
	{
		PinFetch.ResetForTests();
		string published = new string('c', 64);
		Assert.NotEqual(PinGate.LivePin, published);
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		byte[] local = Encoding.UTF8.GetBytes("stale-marble");
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "norcom-marble-1.0.0.jar"), local);
		string expected = new string('d', 64);
		string dirPin = Full(published, "[{\"name\":\"norcom-marble-1.0.0.jar\",\"sha256\":\"" + expected + "\",\"size\":" + local.Length + "}]");
		using HttpClient http = new(new OrderHandler(new List<string>(), dirPin));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: true);
		Assert.Equal("mismatch", result.State);
		Assert.Equal(published, result.Pin);
		Assert.Equal(published, PinGate.ReadAppliedPin(dir));
		Assert.NotEqual(PinGate.LivePin, PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task TempBridgeIsAdoptedOnlyIfItIsThePublishedDocument()
	{
		PinFetch.ResetForTests();
		string temp = "07983921" + new string('a', 56);
		string dir = NewDir();
		using HttpClient http = new(new OrderHandler(new List<string>(), Full(temp, null)));
		PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(http, dir, CancellationToken.None, forceTip: true);
		Assert.Equal(temp, result.Pin);
		Assert.Equal(temp, PinGate.ReadAppliedPin(dir));
		Assert.NotEqual(PinGate.LivePin, result.Pin);
	}

	[Fact]
	public async Task BakedPinDifferenceDoesNotRefuseWhenThePublishedJarMatches()
	{
		string published = new string('e', 64);
		Assert.NotEqual(PinGate.LivePin, published);
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "mods"));
		byte[] fabric = Encoding.UTF8.GetBytes("fabric-bytes");
		await File.WriteAllBytesAsync(Path.Combine(dir, "mods", "fabric-api.jar"), fabric);
		string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fabric)).ToLowerInvariant();
		string pinJson = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + published + "\",\"jars\":[{\"name\":\"fabric-api.jar\",\"sha256\":\"" + sha + "\",\"size\":" + fabric.Length + "}]}";
		string path = Path.Combine(dir, "pin.json");
		await File.WriteAllTextAsync(path, pinJson);
		string? previous = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		try
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", path);
			using HttpClient http = new(new OrderHandler(new List<string>(), Full(published, null)));
			await PinGate.EnforceAsync(http, dir, TomlLite.Parse("version = \"1.0.35\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n"), CancellationToken.None);
			Assert.Equal(published, PinGate.ReadAppliedPin(dir));
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_PIN_PATH", previous);
		}
	}

	private static string Full(string pin, string? jars)
	{
		return "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":" + (jars ?? "[]") + "}";
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-fetch-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private sealed class OrderHandler : HttpMessageHandler
	{
		private readonly List<string> _urls;
		private readonly string? _commitRefBody;
		private readonly string? _jsDelivrBody;
		private readonly string? _contentsBody;

		public OrderHandler(List<string> urls, string? commitRefBody, string? jsDelivrBody = null, string? contentsBody = null)
		{
			_urls = urls;
			_commitRefBody = commitRefBody;
			_jsDelivrBody = jsDelivrBody;
			_contentsBody = contentsBody;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			_urls.Add(url);
			string? body = null;
			if (url.Contains("/commits/main", StringComparison.Ordinal))
			{
				body = "{\"sha\":\"" + PinFetch.KnownTip + "\"}";
			}
			else if (url.Contains(PinFetch.KnownTip, StringComparison.Ordinal) && url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
			{
				body = _commitRefBody;
			}
			else if (url.Contains("cdn.jsdelivr.net", StringComparison.Ordinal))
			{
				body = _jsDelivrBody;
			}
			else if (url.Contains("/contents/meta/", StringComparison.Ordinal))
			{
				body = _contentsBody;
			}
			if (string.IsNullOrEmpty(body))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			});
		}
	}
}
