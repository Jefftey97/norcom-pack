using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class FreshPackSyncTests
{
	[Fact]
	public async Task EmptyInstanceWithAMatchingPinStillSyncsEveryModBeforePlay()
	{
		PinFetch.ResetForTests();
		string? previous = Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
		string home = Path.Combine(Path.GetTempPath(), "norcom-fresh-" + Guid.NewGuid().ToString("n"));
		Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", home);
		string root = Path.Combine(home, "instance");
		string game = Path.Combine(root, "game");
		string cache = Path.Combine(root, "cache");
		string managed = Path.Combine(root, "managed.json");
		Directory.CreateDirectory(game);
		Directory.CreateDirectory(cache);
		await File.WriteAllTextAsync(Path.Combine(game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
		byte[] fabric = Encoding.UTF8.GetBytes("fabric-api-bytes");
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		string fabricToml = Meta("fabric-api-0.165.2+26.3.jar", "https://cdn.example/fabric-api.jar", fabric);
		string sodiumToml = Meta("sodium.jar", "https://cdn.example/sodium.jar", sodium);
		byte[] fabricMeta = Encoding.UTF8.GetBytes(fabricToml);
		byte[] sodiumMeta = Encoding.UTF8.GetBytes(sodiumToml);
		string indexText = "hash-format = \"sha256\"\n[[files]]\nfile = \"mods/fabric-api-0.165.2+26.3.pw.toml\"\nhash = \"" + Hex(fabricMeta) + "\"\n[[files]]\nfile = \"mods/sodium.pw.toml\"\nhash = \"" + Hex(sodiumMeta) + "\"\n";
		byte[] index = Encoding.UTF8.GetBytes(indexText);
		string pack = "name = \"NORCOM\"\nversion = \"1\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + Hex(index) + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
		string pin = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + PinGate.LivePin + "\",\"jars\":[]}";
		RecordingHandler handler = new RecordingHandler(pack, index, fabricMeta, sodiumMeta, fabric, sodium, pin);
		try
		{
			using HttpClient http = new HttpClient(handler);
			await new PackSyncService(http).SyncAsync(game, managed, cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
			Assert.Equal("fabric-api-bytes", await File.ReadAllTextAsync(Path.Combine(game, "mods", "fabric-api-0.165.2+26.3.jar")));
			Assert.Equal("sodium-bytes", await File.ReadAllTextAsync(Path.Combine(game, "mods", "sodium.jar")));
			Assert.Contains("Pack sync complete", await File.ReadAllTextAsync(NorcomPaths.LogPath), StringComparison.Ordinal);
			Assert.True(PackSyncService.HasSyncRecord(game, managed));
			handler.Urls.Clear();
			await new PackSyncService(http).SyncAsync(game, managed, cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
			Assert.DoesNotContain(handler.Urls, url => url.Contains("fabric-api.jar", StringComparison.Ordinal) || url.Contains("sodium.jar", StringComparison.Ordinal) || url.EndsWith("index.toml", StringComparison.Ordinal));
			Assert.Contains("pack resync skipped", await File.ReadAllTextAsync(NorcomPaths.LogPath), StringComparison.Ordinal);
			File.Delete(Path.Combine(game, "mods", "fabric-api-0.165.2+26.3.jar"));
			PinMismatchException missing = Assert.Throws<PinMismatchException>(() => PackSyncService.RequireMods(game, managed));
			Assert.Contains("fabric-api-0.165.2+26.3.jar", missing.Message, StringComparison.Ordinal);
			Assert.Contains("Minecraft was not started", missing.Message, StringComparison.Ordinal);
			Assert.False(PackSyncService.HasSyncRecord(game, managed));
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", previous);
			PinFetch.ResetForTests();
		}
	}

	private static string Meta(string file, string url, byte[] body)
	{
		return "filename = \"" + file + "\"\nside = \"both\"\n[download]\nurl = \"" + url + "\"\nhash-format = \"sha256\"\nhash = \"" + Hex(body) + "\"\n";
	}

	private static string Hex(byte[] bytes)
	{
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	private sealed class RecordingHandler : HttpMessageHandler
	{
		private readonly string _pack;
		private readonly byte[] _index;
		private readonly byte[] _fabricMeta;
		private readonly byte[] _sodiumMeta;
		private readonly byte[] _fabric;
		private readonly byte[] _sodium;
		private readonly string _pin;

		public RecordingHandler(string pack, byte[] index, byte[] fabricMeta, byte[] sodiumMeta, byte[] fabric, byte[] sodium, string pin)
		{
			_pack = pack;
			_index = index;
			_fabricMeta = fabricMeta;
			_sodiumMeta = sodiumMeta;
			_fabric = fabric;
			_sodium = sodium;
			_pin = pin;
		}

		public List<string> Urls { get; } = new List<string>();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			Urls.Add(url);
			if (url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
			{
				return Task.FromResult(Text(_pin));
			}
			if (url.EndsWith("pack.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Text(_pack));
			}
			if (url.EndsWith("index.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_index));
			}
			if (url.Contains("fabric-api-0.165.2+26.3.pw.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_fabricMeta));
			}
			if (url.Contains("sodium.pw.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_sodiumMeta));
			}
			if (url.Contains("fabric-api.jar", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_fabric));
			}
			if (url.Contains("sodium.jar", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_sodium));
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		private static HttpResponseMessage Text(string body)
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "text/plain")
			};
		}

		private static HttpResponseMessage Bytes(byte[] body)
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(body)
			};
		}
	}
}
