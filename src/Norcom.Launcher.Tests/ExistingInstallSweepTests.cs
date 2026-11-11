using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ExistingInstallSweepTests
{
	[Fact]
	public async Task PlayResyncUpdateAndFirstRunLeaveExistingSettingsUntouched()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "config"));
		Directory.CreateDirectory(Path.Combine(dir, "shaderpacks"));
		Directory.CreateDirectory(Path.Combine(dir, "resourcepacks"));
		File.WriteAllText(Path.Combine(dir, "options.txt"), "renderDistance:9\nmaxFps:80\nkey_key.attack:key.mouse.left\nresourcePacks:[\"vanilla\",\"file/InGame.zip\",\"file/norcom_marble.zip\"]\n");
		File.WriteAllText(Path.Combine(dir, "config", "iris.properties"), "shaderPack=MyShader.zip\nmaxShadowRenderDistance=5\n");
		File.WriteAllText(Path.Combine(dir, "config", "sodium-options.json"), "{\"performance\":{\"chunk_builder_threads\":3}}\n");
		File.WriteAllText(Path.Combine(dir, "config", "DistantHorizons.toml"), "# player\nlodChunkRenderDistanceRadius = 48\n");
		File.WriteAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), "profile=CUSTOM\nRP_MODE=2\n");
		File.WriteAllText(Path.Combine(dir, "resourcepacks", "norcom_marble.zip"), "marble-bytes");
		Dictionary<string, byte[]> before = Snapshot(dir);
		GraphicsPlay.Probe = () => null;
		string? previousPack = Environment.GetEnvironmentVariable("NORCOM_PACK_URL");
		Environment.SetEnvironmentVariable("NORCOM_PACK_URL", "https://pack.example/pack.toml");
		try
		{
			for (int i = 0; i < 2; i++)
			{
				Play(dir, TexturePackChoice.Patrix);
				await Resync(dir);
			}
			await UpdateFeed(dir);
			Play(dir, "");
		}
		finally
		{
			GraphicsPlay.Probe = null;
			Environment.SetEnvironmentVariable("NORCOM_PACK_URL", previousPack);
		}
		Dictionary<string, byte[]> after = Snapshot(dir);
		Assert.Equal(before.Keys.OrderBy(key => key), after.Keys.OrderBy(key => key));
		foreach (string key in before.Keys)
		{
			Assert.Equal(before[key], after[key]);
		}
		Assert.Equal("marble-bytes", File.ReadAllText(Path.Combine(dir, "resourcepacks", "norcom_marble.zip")));
		Assert.False(Directory.Exists(Path.Combine(dir, "resourcepacks", "norcom-held")));
		string cfg = Path.Combine(dir, "instance.cfg");
		File.WriteAllText(cfg, "[General]\nMinMemAlloc=3072\nMaxMemAlloc=3072\n");
		PrismInstance.WriteConfig(cfg, "java", 8192, applyMemory: false);
		string kept = File.ReadAllText(cfg);
		Assert.Contains("MinMemAlloc=3072", kept, StringComparison.Ordinal);
		Assert.Contains("MaxMemAlloc=3072", kept, StringComparison.Ordinal);
		Assert.DoesNotContain("8192", kept, StringComparison.Ordinal);
	}

	[Fact]
	public void PlayWithoutARamChangeLeavesZomboidMemoryAndOptions()
	{
		string root = NewDir();
		string steam = Path.Combine(root, "steam");
		string game = Path.Combine(steam, "steamapps", "common", "ProjectZomboid");
		Directory.CreateDirectory(game);
		string options = Path.Combine(game, "options.ini");
		File.WriteAllText(options, "frameRate=144\nkeyBind=custom\n");
		byte[] optionsBytes = File.ReadAllBytes(options);
		string user = Path.Combine(root, "user");
		Directory.CreateDirectory(Path.Combine(user, "Zomboid"));
		string json = "{\"vmArgs\":[\"-Xmx3072m\"]}";
		string memory = Path.Combine(user, "Zomboid", ZomboidMemory.FileName);
		File.WriteAllText(memory, json);
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[Path.Combine(steam, "steamapps", "libraryfolders.vdf")] = "\"path\" \"" + steam.Replace("\\", "\\\\") + "\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game };
		SteamSources sources = new SteamSources
		{
			RegistryPaths = new[] { steam },
			FileExists = path => files.ContainsKey(path),
			DirectoryExists = path => dirs.Contains(path),
			ReadText = path => files.TryGetValue(path, out string text) ? text : null
		};
		ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(sources, user, NewDir(), "", "{\"connect\":{\"host\":\"play.example.com\",\"port\":16261}}", "", 0);
		Assert.True(plan.CanStart);
		Assert.DoesNotContain("-Xmx", plan.Url, StringComparison.Ordinal);
		Assert.Equal(optionsBytes, File.ReadAllBytes(options));
		Assert.Equal(json, File.ReadAllText(memory));
	}

	private static void Play(string dir, string savedChoice)
	{
		bool saved = savedChoice.Length > 0;
		TexturePackChoice.PrepareForLaunch(dir, savedChoice, saved, pendingApply: false);
		DistantHorizonsDefaults.EnsureFresh(dir, GraphicsTier.High);
		GameLaunchGraphics.ApplyBeforeLaunch("minecraft", dir, NewDir(), "", NewDir());
		GraphicsPlay.ApplyAuto(dir);
		PrismPlay.Prepare(null, Array.Empty<string>(), NewDir(), dir, "java", 8192, applyMemory: false);
	}

	private static async Task Resync(string dir)
	{
		await Sync(dir);
	}

	private static async Task UpdateFeed(string dir)
	{
		await PinGate.EnforceFeedAsync(Client(), dir, CancellationToken.None);
	}

	private static async Task Sync(string dir)
	{
		string root = NewDir();
		await new PackSyncService(Client()).SyncAsync(dir, Path.Combine(root, "managed.json"), Path.Combine(root, "cache"), "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
	}

	private static HttpClient Client()
	{
		byte[] index = Encoding.UTF8.GetBytes("hash-format = \"sha256\"\n");
		string indexHash = Convert.ToHexString(SHA256.HashData(index)).ToLowerInvariant();
		string pack = "name = \"NORCOM\"\nversion = \"1\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + indexHash + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
		return new HttpClient(new PackHandler(Encoding.UTF8.GetBytes(pack), index));
	}

	private static Dictionary<string, byte[]> Snapshot(string dir)
	{
		string[] names =
		{
			"options.txt",
			"config/iris.properties",
			"config/sodium-options.json",
			"config/DistantHorizons.toml",
			"shaderpacks/ComplementaryUnbound_r5.9.3.txt",
			"resourcepacks/norcom_marble.zip"
		};
		Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		for (int i = 0; i < names.Length; i++)
		{
			files[names[i]] = File.ReadAllBytes(Path.Combine(dir, names[i].Replace('/', Path.DirectorySeparatorChar)));
		}
		return files;
	}

	private sealed class PackHandler : HttpMessageHandler
	{
		private readonly byte[] _pack;
		private readonly byte[] _index;

		public PackHandler(byte[] pack, byte[] index)
		{
			_pack = pack;
			_index = index;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			if (url.EndsWith("pack.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_pack) });
			}
			if (url.EndsWith("index.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_index) });
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-sweep-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
