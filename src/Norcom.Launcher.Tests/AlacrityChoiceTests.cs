using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class AlacrityChoiceTests
{
	private const string OfficialUrl = "https://cdn.modrinth.com/data/PUUpX2qq/versions/Kc2ktOzs/Alacrity.zip";

	private const string OfficialSha512 = "0b0faa45553153000b4cbb187672f49d06e19b4212f3c490964cc91e6e97767ba00ab42a30c4b77ab52994883f9a2a1251f98fd383d59cfa07ce5793d1452822";

	private const string PatrixList = "[\"vanilla\",\"continuity:default\",\"continuity:glass_pane_culling_fix\",\"file/Patrix_26.3_32x_basic.zip\",\"file/Patrix_26.3_32x_addon.zip\",\"file/Patrix_26.3_models.zip\"]";

	public AlacrityChoiceTests()
	{
		KnownVisualAssets.Assets = () => KnownVisualAssets.All;
	}

	[Fact]
	public void OfficialAlacrityPinIsTheModrinthFileAndIsNotInTheRepo()
	{
		VisualAsset asset = Assert.Single(KnownVisualAssets.All, item => item.RelativePath.EndsWith("/Alacrity.zip", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(OfficialUrl, KnownVisualAssets.AlacrityUrl);
		Assert.Equal(OfficialSha512, KnownVisualAssets.AlacritySha512);
		Assert.Equal(OfficialUrl, asset.Url);
		Assert.Equal(OfficialSha512, asset.Sha512);
		string root = RepoRoot();
		Assert.False(File.Exists(Path.Combine(root, "Alacrity.zip")));
		Assert.False(File.Exists(Path.Combine(root, "resourcepacks", "Alacrity.zip")));
	}

	[Fact]
	public void AlacrityListAddsTheCandleFixAndMarbleAndStripsPatrix()
	{
		string list = TexturePackChoice.BuildList(PatrixList, TexturePackChoice.Alacrity);
		Assert.Equal(GraphicsPack.AlacrityPacks, list);
		Assert.Equal("[\"vanilla\",\"continuity:default\",\"file/Alacrity.zip\",\"file/norcom-alacrity-candle-glass-fix.zip\",\"file/norcom_marble.zip\"]", list);
		Assert.DoesNotContain("Patrix", list, StringComparison.Ordinal);
		Assert.DoesNotContain(GraphicsPack.NeverEnable, list, StringComparison.Ordinal);
	}

	[Fact]
	public void PatrixListStaysTheCurrentPackOrder()
	{
		Assert.Equal("[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\",\"file/Patrix_26.3_32x_addon.zip\",\"file/Patrix_26.3_models.zip\",\"continuity:glass_pane_culling_fix\"]", TexturePackChoice.BuildList(PatrixList, TexturePackChoice.Patrix));
		string fromAlacrity = TexturePackChoice.BuildList(GraphicsPack.AlacrityPacks, TexturePackChoice.Patrix);
		Assert.Equal("[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\",\"file/Patrix_26.3_32x_addon.zip\",\"file/Patrix_26.3_models.zip\",\"file/norcom_marble.zip\"]", fromAlacrity);
		Assert.DoesNotContain("file/Alacrity.zip", fromAlacrity, StringComparison.Ordinal);
		Assert.DoesNotContain("norcom-alacrity-candle-glass-fix.zip", fromAlacrity, StringComparison.Ordinal);
		Assert.Equal(TexturePackChoice.Alacrity, TexturePackChoice.Detect(""));
		Assert.Equal(TexturePackChoice.Patrix, TexturePackChoice.Detect(PatrixList));
		Assert.False(KnownVisualAssets.WantsAlacrity(NewDir()));
	}

	[Fact]
	public async Task ChoosingPatrixDoesNotDownloadAlacrity()
	{
		string dir = NewDir();
		string options = "renderDistance:12\nresourcePacks:" + PatrixList + "\nkey_key.attack:key.mouse.left\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		RecordingHttp http = new RecordingHttp(_ => throw new HttpRequestException("offline"));
		await TexturePackActivation.EnableAsync(dir, TexturePackChoice.Patrix, http.Client, new TransferMeter(), CancellationToken.None);
		Assert.Empty(http.Urls);
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
		string updated = File.ReadAllText(Path.Combine(dir, "options.txt"));
		Assert.Contains("file/Patrix_26.3_32x_basic.zip", updated, StringComparison.Ordinal);
		Assert.DoesNotContain("file/Alacrity.zip", updated, StringComparison.Ordinal);
		Assert.Contains("renderDistance:12", updated, StringComparison.Ordinal);
		Assert.Contains("key_key.attack:key.mouse.left", updated, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ChoosingAlacrityDownloadsTheOfficialFileThenWritesTheFix()
	{
		byte[] body = Encoding.UTF8.GetBytes("alacrity-stand-in");
		string hash = Sha512(body);
		UseStandIn(hash);
		string dir = NewDir();
		string options = "renderDistance:12\nresourcePacks:" + PatrixList + "\nincompatibleResourcePacks:[\"file/norcom-alacrity-candle-glass-fix.zip\",\"file/norcom_marble.zip\"]\nkey_key.attack:key.mouse.left\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		RecordingHttp http = new RecordingHttp(url =>
		{
			Assert.Equal(OfficialUrl, url);
			return Bytes(body);
		});
		await TexturePackActivation.EnableAsync(dir, TexturePackChoice.Alacrity, http.Client, new TransferMeter(), CancellationToken.None);
		Assert.Equal(new[] { OfficialUrl }, http.Urls);
		Assert.Equal(body, File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
		string overlay = Path.Combine(dir, "resourcepacks", GraphicsPack.OverlayFile);
		Assert.Equal(GraphicsPack.OverlaySha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(overlay))).ToLowerInvariant());
		string updated = File.ReadAllText(Path.Combine(dir, "options.txt"));
		Assert.Contains("resourcePacks:" + GraphicsPack.AlacrityPacks, updated, StringComparison.Ordinal);
		Assert.DoesNotContain("Patrix", updated, StringComparison.Ordinal);
		Assert.DoesNotContain(GraphicsPack.NeverEnable, updated, StringComparison.Ordinal);
		Assert.Contains("renderDistance:12", updated, StringComparison.Ordinal);
		Assert.Contains("key_key.attack:key.mouse.left", updated, StringComparison.Ordinal);
		Assert.DoesNotContain("norcom-alacrity-candle-glass-fix.zip", ReadValue(updated, "incompatibleResourcePacks"), StringComparison.Ordinal);
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip.norcom-download")));
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip.norcom-download.part")));
	}

	[Fact]
	public async Task AMatchingAlacrityFileIsNotDownloadedAgain()
	{
		byte[] body = Encoding.UTF8.GetBytes("already-here");
		UseStandIn(Sha512(body));
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "resourcepacks"));
		File.WriteAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip"), body);
		File.WriteAllText(Path.Combine(dir, "options.txt"), "resourcePacks:" + PatrixList + "\n");
		RecordingHttp http = new RecordingHttp(_ => throw new HttpRequestException("should not be called"));
		await TexturePackActivation.EnableAsync(dir, TexturePackChoice.Alacrity, http.Client, new TransferMeter(), CancellationToken.None);
		Assert.Empty(http.Urls);
		Assert.Equal(body, File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
		Assert.Contains(GraphicsPack.AlacrityPacks, File.ReadAllText(Path.Combine(dir, "options.txt")), StringComparison.Ordinal);
	}

	[Fact]
	public async Task HashMismatchLeavesTheOldListAndTheOldZip()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "resourcepacks"));
		File.WriteAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip"), Encoding.UTF8.GetBytes("previous-pack"));
		string options = "renderDistance:9\nresourcePacks:" + PatrixList + "\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		RecordingHttp http = new RecordingHttp(url =>
		{
			Assert.Equal(OfficialUrl, url);
			return Bytes(Encoding.UTF8.GetBytes("not-the-official-zip"));
		});
		InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(() => TexturePackActivation.EnableAsync(dir, TexturePackChoice.Alacrity, http.Client, new TransferMeter(), CancellationToken.None));
		Assert.Contains("did not match the official Modrinth file", ex.Message, StringComparison.Ordinal);
		Assert.Contains("left as it was", ex.Message, StringComparison.Ordinal);
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal("previous-pack", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip"))));
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", GraphicsPack.OverlayFile)));
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip.norcom-download")));
		Assert.Equal(OfficialUrl, Assert.Single(http.Urls));
	}

	[Fact]
	public async Task OfflineAndPartialDownloadsLeaveTheOldList()
	{
		string options = "resourcePacks:" + PatrixList + "\n";
		string offlineDir = NewDir();
		File.WriteAllText(Path.Combine(offlineDir, "options.txt"), options);
		RecordingHttp offline = new RecordingHttp(_ => throw new HttpRequestException("no route"));
		InvalidDataException offlineError = await Assert.ThrowsAsync<InvalidDataException>(() => TexturePackActivation.EnableAsync(offlineDir, TexturePackChoice.Alacrity, offline.Client, new TransferMeter(), CancellationToken.None));
		Assert.Contains("could not be downloaded", offlineError.Message, StringComparison.Ordinal);
		Assert.Contains("left as it was", offlineError.Message, StringComparison.Ordinal);
		Assert.Equal(options, File.ReadAllText(Path.Combine(offlineDir, "options.txt")));
		Assert.False(File.Exists(Path.Combine(offlineDir, "resourcepacks", "Alacrity.zip")));

		string partialDir = NewDir();
		File.WriteAllText(Path.Combine(partialDir, "options.txt"), options);
		RecordingHttp partial = new RecordingHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new PartialContent() });
		InvalidDataException partialError = await Assert.ThrowsAsync<InvalidDataException>(() => TexturePackActivation.EnableAsync(partialDir, TexturePackChoice.Alacrity, partial.Client, new TransferMeter(), CancellationToken.None));
		Assert.Contains("did not finish", partialError.Message, StringComparison.Ordinal);
		Assert.Contains("left as it was", partialError.Message, StringComparison.Ordinal);
		Assert.Equal(options, File.ReadAllText(Path.Combine(partialDir, "options.txt")));
		Assert.False(File.Exists(Path.Combine(partialDir, "resourcepacks", "Alacrity.zip")));
		Assert.False(File.Exists(Path.Combine(partialDir, "resourcepacks", "Alacrity.zip.norcom-download")));
		Assert.False(File.Exists(Path.Combine(partialDir, "resourcepacks", "Alacrity.zip.norcom-download.part")));
	}

	[Fact]
	public async Task GraphicsTierDownloadsAlacrityBeforeChangingTheList()
	{
		byte[] body = Encoding.UTF8.GetBytes("tier-alacrity");
		UseStandIn(Sha512(body));
		string dir = NewDir();
		string options = "renderDistance:4\nresourcePacks:" + PatrixList + "\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		RecordingHttp failed = new RecordingHttp(_ => throw new HttpRequestException("offline"));
		await Assert.ThrowsAsync<InvalidDataException>(() => TexturePackActivation.ApplyTierAsync(dir, GraphicsTier.High, new GraphicsService(), failed.Client, new TransferMeter(), CancellationToken.None));
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));

		RecordingHttp ok = new RecordingHttp(_ => Bytes(body));
		await TexturePackActivation.ApplyTierAsync(dir, GraphicsTier.High, new GraphicsService(), ok.Client, new TransferMeter(), CancellationToken.None);
		Assert.Equal(OfficialUrl, Assert.Single(ok.Urls));
		Assert.Equal(body, File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
		Assert.Contains("resourcePacks:" + GraphicsPack.AlacrityPacks, File.ReadAllText(Path.Combine(dir, "options.txt")), StringComparison.Ordinal);
		Assert.Contains("renderDistance:12", File.ReadAllText(Path.Combine(dir, "options.txt")), StringComparison.Ordinal);
	}

	[Fact]
	public async Task FixVisualsSkipsAlacrityUntilItIsTheSelectedPack()
	{
		byte[] patrix = Encoding.UTF8.GetBytes("patrix-bytes");
		byte[] alacrity = Encoding.UTF8.GetBytes("alacrity-bytes");
		KnownVisualAssets.Assets = () => new[]
		{
			new VisualAsset("resourcepacks/Patrix_26.3_32x_basic.zip", "https://example.test/patrix.zip", Sha512(patrix)),
			new VisualAsset("resourcepacks/Alacrity.zip", OfficialUrl, Sha512(alacrity))
		};
		string dir = NewDir();
		File.WriteAllText(Path.Combine(dir, "options.txt"), "resourcePacks:" + PatrixList + "\n");
		RecordingHttp http = new RecordingHttp(url => url.Contains("patrix", StringComparison.Ordinal) ? Bytes(patrix) : Bytes(alacrity));
		await KnownVisualAssets.EnsureAsync(http.Client, dir, new TransferMeter(), CancellationToken.None);
		Assert.Equal(new[] { "https://example.test/patrix.zip" }, http.Urls);
		Assert.False(File.Exists(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
		Assert.False(KnownVisualAssets.WantsAlacrity(dir));

		File.WriteAllText(Path.Combine(dir, "options.txt"), "resourcePacks:" + GraphicsPack.AlacrityPacks + "\n");
		Assert.True(KnownVisualAssets.WantsAlacrity(dir));
		await KnownVisualAssets.EnsureAsync(http.Client, dir, new TransferMeter(), CancellationToken.None);
		Assert.Contains(OfficialUrl, http.Urls);
		Assert.Equal(alacrity, File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
	}

	[Fact]
	public async Task PlayEnsuresAlacrityOnlyForTheSavedChoice()
	{
		byte[] body = Encoding.UTF8.GetBytes("play-alacrity");
		UseStandIn(Sha512(body));
		string dir = NewDir();
		string options = "resourcePacks:" + PatrixList + "\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		RecordingHttp patrix = new RecordingHttp(_ => throw new HttpRequestException("no"));
		await TexturePackActivation.EnsureSelectedAsync(dir, TexturePackChoice.Patrix, specified: true, patrix.Client, new TransferMeter(), CancellationToken.None);
		Assert.Empty(patrix.Urls);
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));

		RecordingHttp missing = new RecordingHttp(_ => throw new HttpRequestException("offline"));
		await TexturePackActivation.EnsureSelectedAsync(dir, "", specified: false, missing.Client, new TransferMeter(), CancellationToken.None);
		Assert.Empty(missing.Urls);

		RecordingHttp alacrity = new RecordingHttp(_ => Bytes(body));
		await TexturePackActivation.EnsureSelectedAsync(dir, TexturePackChoice.Alacrity, specified: true, alacrity.Client, new TransferMeter(), CancellationToken.None);
		Assert.Equal(OfficialUrl, Assert.Single(alacrity.Urls));
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal(body, File.ReadAllBytes(Path.Combine(dir, "resourcepacks", "Alacrity.zip")));
	}

	[Fact]
	public async Task PackSyncKeepsAFriendsAlacrityChoice()
	{
		string dir = NewDir();
		string chosen = "renderDistance:14\nresourcePacks:" + GraphicsPack.AlacrityPacks + "\nkey_key.attack:key.mouse.left\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), chosen);
		await SyncOptions(dir, "renderDistance:12\nresourcePacks:" + PatrixList + "\n");
		Assert.Equal(chosen, File.ReadAllText(Path.Combine(dir, "options.txt")));

		string fresh = NewDir();
		string shipped = "renderDistance:12\nresourcePacks:" + PatrixList + "\n";
		await SyncOptions(fresh, shipped);
		Assert.Equal(shipped, File.ReadAllText(Path.Combine(fresh, "options.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
		Assert.False(File.Exists(Path.Combine(fresh, "resourcepacks", "Alacrity.zip")));
	}

	[Fact]
	public void ProductionWiresTheDownloadAndDoesNotRehostAlacrity()
	{
		string app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Norcom.Launcher", "Norcom.Launcher", "App.cs"));
		string shell = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Norcom.Launcher", "Norcom.Launcher.ViewModels", "ShellViewModel.cs"));
		string play = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Norcom.Launcher.Core", "Norcom", "Launcher", "Core", "Games", "Minecraft", "MinecraftGame.cs"));
		Assert.Contains("KnownVisualAssets.EnsureAsync", app, StringComparison.Ordinal);
		Assert.Contains("KnownVisualAssets.EnsureAsync", shell, StringComparison.Ordinal);
		Assert.Contains("TexturePackActivation.EnableAsync", shell, StringComparison.Ordinal);
		Assert.Contains("TexturePackActivation.ApplyTierAsync", shell, StringComparison.Ordinal);
		Assert.Contains("TexturePackActivation.EnsureSelectedAsync", play, StringComparison.Ordinal);
		Assert.DoesNotContain("Alacrity.zip", app, StringComparison.Ordinal);
	}

	private static async Task SyncOptions(string dir, string shipped)
	{
		byte[] options = Encoding.UTF8.GetBytes(shipped);
		string optionsHash = Convert.ToHexString(SHA256.HashData(options)).ToLowerInvariant();
		byte[] index = Encoding.UTF8.GetBytes("hash-format = \"sha256\"\n[[files]]\nfile = \"options.txt\"\nhash = \"" + optionsHash + "\"\npreserve = true\n");
		string indexHash = Convert.ToHexString(SHA256.HashData(index)).ToLowerInvariant();
		string pack = "name = \"NORCOM\"\nversion = \"1\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + indexHash + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
		using RecordingHttp http = new RecordingHttp(url =>
		{
			Assert.DoesNotContain("modrinth.com", url, StringComparison.OrdinalIgnoreCase);
			if (url.EndsWith("pack.toml", StringComparison.Ordinal))
			{
				return Bytes(Encoding.UTF8.GetBytes(pack));
			}
			if (url.EndsWith("index.toml", StringComparison.Ordinal))
			{
				return Bytes(index);
			}
			if (url.EndsWith("options.txt", StringComparison.Ordinal))
			{
				return Bytes(options);
			}
			return new HttpResponseMessage(HttpStatusCode.NotFound);
		});
		string root = NewDir();
		await new PackSyncService(http.Client).SyncAsync(dir, Path.Combine(root, "managed.json"), Path.Combine(root, "cache"), "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
	}

	private static void UseStandIn(string hash)
	{
		KnownVisualAssets.Assets = () => new[] { new VisualAsset("resourcepacks/Alacrity.zip", OfficialUrl, hash) };
	}

	private static string Sha512(byte[] data)
	{
		return Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();
	}

	private static HttpResponseMessage Bytes(byte[] body)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
	}

	private static string ReadValue(string options, string key)
	{
		foreach (string line in options.Split('\n'))
		{
			if (line.StartsWith(key + ":", StringComparison.Ordinal))
			{
				return line.Substring(key.Length + 1);
			}
		}
		return "";
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-alacrity-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static string RepoRoot()
	{
		string dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			if (Directory.Exists(Path.Combine(dir, "src", "Norcom.Launcher.Core")) && Directory.Exists(Path.Combine(dir, "src", "Norcom.Launcher")))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir);
		}
		throw new DirectoryNotFoundException("norcom-pack");
	}

	private sealed class RecordingHttp : IDisposable
	{
		private readonly Handler _handler;

		public RecordingHttp(Func<string, HttpResponseMessage> respond)
		{
			_handler = new Handler(respond);
			Client = new HttpClient(_handler);
		}

		public HttpClient Client { get; }

		public IReadOnlyList<string> Urls => _handler.Urls;

		public void Dispose()
		{
			Client.Dispose();
		}

		private sealed class Handler : HttpMessageHandler
		{
			private readonly Func<string, HttpResponseMessage> _respond;

			public Handler(Func<string, HttpResponseMessage> respond)
			{
				_respond = respond;
			}

			public List<string> Urls { get; } = new List<string>();

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				string url = request.RequestUri?.AbsoluteUri ?? "";
				Urls.Add(url);
				try
				{
					return Task.FromResult(_respond(url));
				}
				catch (Exception ex)
				{
					return Task.FromException<HttpResponseMessage>(ex);
				}
			}
		}
	}

	private sealed class PartialContent : HttpContent
	{
		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			stream.Write(new byte[] { 1, 2, 3, 4 });
			throw new IOException("truncated");
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 32;
			return true;
		}
	}
}
