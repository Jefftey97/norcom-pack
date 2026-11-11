using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Xunit;

namespace Norcom.Launcher.Tests;

public class UpgradePlayTests
{
	private const string OldMarblePin = "0517b6b64a967471914d12f7567823ac8a329e7d7e2200575b841350fc3afc48";

	private const string Pack = """
		version = "1.0.40"
		[versions]
		minecraft = "26.3"
		fabric = "0.20.1"
		""";

	[Fact]
	public void UpgradeFrom2044ClearsStalePackStateAndKeepsPlayerFiles()
	{
		(string home, string game, string launcher) = Roots();
		Write2044State(home, game, launcher);
		string[] keepNames = { PinGate.MarbleJarName, "sodium.jar" };
		string[] keepPaths = { "mods/" + PinGate.MarbleJarName, "mods/sodium.jar" };

		IReadOnlyList<string> deleted = PackUpgrade.OnStartup(home, game, launcher, NorcomVersion.Current, keepNames, keepPaths);

		Assert.Equal(NorcomVersion.Current, File.ReadAllText(Path.Combine(launcher, PackUpgrade.VersionFileName)).Trim());
		Assert.DoesNotContain(deleted, path => IsPlayerFile(game, home, path));
		Assert.Contains(Path.Combine(home, "cache", "packwiz", "index.toml"), deleted);
		Assert.Contains(Path.Combine(home, "managed-files.json"), deleted);
		Assert.Contains(Path.Combine(launcher, PinGate.PrimaryAppliedPinFile), deleted);
		Assert.Contains(Path.Combine(game, PinGate.AppliedPinFile), deleted);
		Assert.Contains(Path.Combine(game, "mods", "old-removed.jar"), deleted);
		Assert.False(File.Exists(Path.Combine(home, "cache", "packwiz", "index.toml")));
		Assert.False(File.Exists(Path.Combine(home, "managed-files.json")));
		Assert.False(File.Exists(Path.Combine(launcher, PinGate.PrimaryAppliedPinFile)));
		Assert.False(File.Exists(Path.Combine(game, PinGate.AppliedPinFile)));
		Assert.False(File.Exists(Path.Combine(game, "mods", "old-removed.jar")));
		Assert.Equal("new-marble", File.ReadAllText(Path.Combine(game, "mods", PinGate.MarbleJarName)));
		Assert.Equal("sodium", File.ReadAllText(Path.Combine(game, "mods", "sodium.jar")));
		AssertPlayerFiles(home, game);
		Assert.Empty(PackUpgrade.OnStartup(home, game, launcher, NorcomVersion.Current, keepNames, keepPaths));
		Assert.Equal("sodium", File.ReadAllText(Path.Combine(game, "mods", "sodium.jar")));
	}

	[Fact]
	public void CleanupCanDeleteOnlyPackStateAndRetiredMods()
	{
		Assert.Equal(new[] { "cache", "managed-files.json", "Launcher/applied-pin.json", "norcom-applied-pin.txt" }, PackUpgrade.DeletableState);
		Assert.True(PackUpgrade.CanRetire("mods/old-removed.jar"));
		Assert.False(PackUpgrade.CanRetire("saves/world/level.dat"));
		Assert.False(PackUpgrade.CanRetire("options.txt"));
		Assert.False(PackUpgrade.CanRetire("shaderpacks/ComplementaryUnbound_r5.9.3.zip"));
		Assert.False(PackUpgrade.CanRetire("resourcepacks/Faithful.zip"));
		Assert.False(PackUpgrade.CanRetire("screenshots/shot.png"));
		Assert.False(PackUpgrade.CanRetire("config/DistantHorizons.toml"));
		Assert.False(PackUpgrade.CanRetire("config/player.toml"));
		Assert.True(PackUpgrade.IsProtected("config/DistantHorizons.toml"));
	}

	[Fact]
	public async Task RateLimitedContentsUsesThePublishedPinInsteadOfStaleKnownTip()
	{
		PinFetch.ResetForTests();
		string game = NewDir();
		Directory.CreateDirectory(Path.Combine(game, "mods"));
		byte[] marble = Encoding.UTF8.GetBytes("marble-f99e");
		await File.WriteAllBytesAsync(Path.Combine(game, "mods", PinGate.MarbleJarName), marble);
		using HttpClient http = new(new PinRoutes(Published(marble), Stale(Encoding.UTF8.GetBytes("marble-3566420d")), contents: HttpStatusCode.TooManyRequests, raw: HttpStatusCode.OK, known: HttpStatusCode.OK));
		await PinGate.EnforceAsync(http, game, TomlLite.Parse(Pack), CancellationToken.None);
		Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(game));
		Assert.NotEqual(OldMarblePin, PinGate.ReadAppliedPin(game));
		Assert.Equal("marble-f99e", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(game, "mods", PinGate.MarbleJarName))));
	}

	[Fact]
	public async Task StaleKnownTipAloneDoesNotBlockAndASyncedPackDoesNotResync()
	{
		PinFetch.ResetForTests();
		string game = NewDir();
		Directory.CreateDirectory(Path.Combine(game, "mods"));
		byte[] marble = Encoding.UTF8.GetBytes("marble-f99e");
		await File.WriteAllBytesAsync(Path.Combine(game, "mods", PinGate.MarbleJarName), marble);
		await File.WriteAllTextAsync(Path.Combine(game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
		using HttpClient http = new(new PinRoutes(Published(marble), Stale(Encoding.UTF8.GetBytes("marble-3566420d")), contents: HttpStatusCode.TooManyRequests, raw: HttpStatusCode.TooManyRequests, known: HttpStatusCode.OK));
		PinGate.PinCheck check = await PinGate.CheckAsync(http, game, TomlLite.Parse(Pack), CancellationToken.None, null, null);
		Assert.Equal(PinGate.PinCheck.Open, check);
		Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(game));
		int resyncs = 0;
		await PinHeal.RunAsync(http, game, TomlLite.Parse(Pack), null, _ =>
		{
			resyncs++;
			return Task.CompletedTask;
		}, CancellationToken.None, packSynced: true);
		Assert.Equal(0, resyncs);
	}

	[Fact]
	public async Task MissingPrismInstanceReachesPrepare()
	{
		PinFetch.ResetForTests();
		string root = NewDir();
		string game = Path.Combine(root, "game");
		Directory.CreateDirectory(game);
		Assert.False(PrismInstance.HasNorcomInstance(root));
		Directory.CreateDirectory(Path.Combine(root, "instances", "NORCOM"));
		File.WriteAllText(Path.Combine(root, "instances", "NORCOM", "instance.cfg"), "[General]\nname=NORCOM\n");
		Assert.True(PrismInstance.HasNorcomInstance(root));
		File.Delete(Path.Combine(root, "instances", "NORCOM", "instance.cfg"));
		Assert.False(PrismInstance.HasNorcomInstance(root));
		int prepares = 0;
		using HttpClient http = new(new PinRoutes(Published(Encoding.UTF8.GetBytes("marble-f99e")), Stale(Encoding.UTF8.GetBytes("marble-3566420d")), HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK));
		PlayOutcome outcome = await PlayConductor.MinecraftAsync(new MinecraftPlayRequest
		{
			LockPath = Path.Combine(root, "missing.lock"),
			ExistingInstall = false,
			EnsureJava = _ => Task.FromResult("java"),
			SyncPack = async token =>
			{
				await PinHeal.RunAsync(http, game, TomlLite.Parse(Pack), null, _ => throw new PinMismatchException(PinGate.BlockCopy), token, packSynced: false, instancePresent: false);
			},
			PreparePrism = _ =>
			{
				prepares++;
				return new PrismPreparation { CanStart = true, Executable = "prism.exe", Arguments = Array.Empty<string>(), Message = PrismMessages.Opening };
			},
			RunPrism = (prepared, _) => Task.FromResult(prepared)
		}, CancellationToken.None);
		Assert.Equal(1, prepares);
		Assert.True(outcome.Started);
		Assert.Contains(PrismMessages.Opening, outcome.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void FirstClientIsDetectedWhenItsParentHasExited()
	{
		const string instance = @"C:\Prism\instances\NORCOM";
		const string game = @"C:\Users\friend\AppData\Roaming\.norcom";
		GameProcessRow client = new GameProcessRow("javaw.exe", "javaw net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir " + game, 7002, 4242, "");
		GameProcessRow server = new GameProcessRow("java.exe", @"G:\NORCOM\Server\java\java.exe -jar fabric-server-launch.jar nogui", 80, 70, @"G:\NORCOM\Server");
		Assert.Equal(7002, PrismGameSignals.SelectGamePid(new[] { server, client }, instance, 5100, game));
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { server }, instance, 5100, game));
	}

	[Fact]
	public void FirstDownloadAfterAMissingInstanceCountsAsMoving()
	{
		string root = NewDir();
		string game = Path.Combine(root, "game");
		Directory.CreateDirectory(game);
		LivePrismGameSignals signals = new LivePrismGameSignals(root, game);
		Assert.False(signals.DownloadMoving());
		Directory.CreateDirectory(Path.Combine(root, "meta"));
		File.WriteAllText(Path.Combine(root, "meta", "net.minecraft.json"), "first");
		Assert.True(signals.DownloadMoving());
	}

	private static void Write2044State(string home, string game, string launcher)
	{
		Directory.CreateDirectory(Path.Combine(home, "cache", "packwiz"));
		Directory.CreateDirectory(Path.Combine(game, "mods"));
		Directory.CreateDirectory(Path.Combine(game, "saves", "world"));
		Directory.CreateDirectory(Path.Combine(game, "shaderpacks"));
		Directory.CreateDirectory(Path.Combine(game, "resourcepacks"));
		Directory.CreateDirectory(Path.Combine(game, "screenshots"));
		Directory.CreateDirectory(Path.Combine(game, "config"));
		Directory.CreateDirectory(launcher);
		File.WriteAllText(Path.Combine(home, "cache", "packwiz", "index.toml"), "old-index");
		File.WriteAllText(Path.Combine(game, "mods", PinGate.MarbleJarName), "new-marble");
		File.WriteAllText(Path.Combine(game, "mods", "sodium.jar"), "sodium");
		File.WriteAllText(Path.Combine(game, "mods", "old-removed.jar"), "removed");
		File.WriteAllText(Path.Combine(game, "saves", "world", "level.dat"), "world");
		File.WriteAllText(Path.Combine(game, "options.txt"), "fov:0.7");
		File.WriteAllText(Path.Combine(game, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip"), "shader");
		File.WriteAllText(Path.Combine(game, "resourcepacks", "Faithful.zip"), "pack");
		File.WriteAllText(Path.Combine(game, "screenshots", "shot.png"), "png");
		File.WriteAllText(Path.Combine(game, "config", "DistantHorizons.toml"), "renderDistance=128");
		File.WriteAllText(Path.Combine(game, "config", "player.toml"), "player-set");
		File.WriteAllText(Path.Combine(home, "minecraft-state.json"), "{\"TexturePack\":\"Alacrity\",\"TexturePackSpecified\":true}");
		File.WriteAllText(Path.Combine(home, "zomboid-feed.json"), "{\"workshop\":[]}");
		File.WriteAllText(Path.Combine(home, "zomboid-state.json"), "{\"RamMb\":8192}");
		File.WriteAllText(Path.Combine(home, "settings.json"), "{\"GraphicsTier\":\"medium\"}");
		File.WriteAllText(Path.Combine(game, PinGate.AppliedPinFile), OldMarblePin + "\n");
		File.WriteAllText(Path.Combine(launcher, PinGate.PrimaryAppliedPinFile), "{\"schema\":1,\"pin_sha256\":\"" + OldMarblePin + "\"}");
		File.WriteAllText(Path.Combine(home, "managed-files.json"), JsonSerializer.Serialize(new[]
		{
			new ManagedEntry("mods/" + PinGate.MarbleJarName, false),
			new ManagedEntry("mods/sodium.jar", false),
			new ManagedEntry("mods/old-removed.jar", false),
			new ManagedEntry("saves/world/level.dat", false),
			new ManagedEntry("options.txt", false),
			new ManagedEntry("shaderpacks/ComplementaryUnbound_r5.9.3.zip", false),
			new ManagedEntry("resourcepacks/Faithful.zip", false),
			new ManagedEntry("screenshots/shot.png", false),
			new ManagedEntry("config/DistantHorizons.toml", false),
			new ManagedEntry("config/player.toml", true)
		}));
	}

	private static void AssertPlayerFiles(string home, string game)
	{
		Assert.Equal("world", File.ReadAllText(Path.Combine(game, "saves", "world", "level.dat")));
		Assert.Equal("fov:0.7", File.ReadAllText(Path.Combine(game, "options.txt")));
		Assert.Equal("shader", File.ReadAllText(Path.Combine(game, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip")));
		Assert.Equal("pack", File.ReadAllText(Path.Combine(game, "resourcepacks", "Faithful.zip")));
		Assert.Equal("png", File.ReadAllText(Path.Combine(game, "screenshots", "shot.png")));
		Assert.Equal("renderDistance=128", File.ReadAllText(Path.Combine(game, "config", "DistantHorizons.toml")));
		Assert.Equal("player-set", File.ReadAllText(Path.Combine(game, "config", "player.toml")));
		Assert.Contains("Alacrity", File.ReadAllText(Path.Combine(home, "minecraft-state.json")));
		Assert.Equal("{\"workshop\":[]}", File.ReadAllText(Path.Combine(home, "zomboid-feed.json")));
		Assert.Equal("{\"RamMb\":8192}", File.ReadAllText(Path.Combine(home, "zomboid-state.json")));
		Assert.Equal("{\"GraphicsTier\":\"medium\"}", File.ReadAllText(Path.Combine(home, "settings.json")));
	}

	private static bool IsPlayerFile(string game, string home, string path)
	{
		string[] kept =
		{
			Path.Combine(game, "saves", "world", "level.dat"),
			Path.Combine(game, "options.txt"),
			Path.Combine(game, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip"),
			Path.Combine(game, "resourcepacks", "Faithful.zip"),
			Path.Combine(game, "screenshots", "shot.png"),
			Path.Combine(game, "config", "DistantHorizons.toml"),
			Path.Combine(game, "config", "player.toml"),
			Path.Combine(game, "mods", PinGate.MarbleJarName),
			Path.Combine(game, "mods", "sodium.jar"),
			Path.Combine(home, "minecraft-state.json"),
			Path.Combine(home, "zomboid-feed.json"),
			Path.Combine(home, "zomboid-state.json"),
			Path.Combine(home, "settings.json")
		};
		return kept.Contains(path, StringComparer.OrdinalIgnoreCase);
	}

	private static string Published(byte[] marble)
	{
		return Pin(PinGate.LivePin, PinGate.LivePinGeneratedAt, marble);
	}

	private static string Stale(byte[] marble)
	{
		return Pin(OldMarblePin, "2026-11-06T13:04:44-07:00", marble);
	}

	private static string Pin(string pin, string generatedAt, byte[] marble)
	{
		string sha = Convert.ToHexString(SHA256.HashData(marble)).ToLowerInvariant();
		return "{\"schema\":1,\"generated_at\":\"" + generatedAt + "\",\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":[{\"name\":\"" + PinGate.MarbleJarName + "\",\"sha256\":\"" + sha + "\",\"size\":" + marble.Length + "}]}";
	}

	private static (string home, string game, string launcher) Roots()
	{
		string home = NewDir();
		return (home, Path.Combine(home, "game"), Path.Combine(home, "Launcher"));
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-upgrade-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private sealed class PinRoutes : HttpMessageHandler
	{
		private readonly string _published;
		private readonly string _stale;
		private readonly HttpStatusCode _contents;
		private readonly HttpStatusCode _raw;
		private readonly HttpStatusCode _known;

		public PinRoutes(string published, string stale, HttpStatusCode contents, HttpStatusCode raw, HttpStatusCode known)
		{
			_published = published;
			_stale = stale;
			_contents = contents;
			_raw = raw;
			_known = known;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			if (url.Contains("/contents/meta/norcom-mod-pin.json", StringComparison.Ordinal))
			{
				return Task.FromResult(Reply(_contents, _published));
			}
			if (url.Contains("/main/meta/norcom-mod-pin.json?t=", StringComparison.Ordinal) || url.Contains("cdn.jsdelivr.net", StringComparison.Ordinal))
			{
				return Task.FromResult(Reply(_raw, _published));
			}
			if (url.Contains(PinFetch.KnownTip, StringComparison.Ordinal) && url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
			{
				return Task.FromResult(Reply(_known, _stale));
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}

		private static HttpResponseMessage Reply(HttpStatusCode status, string body)
		{
			HttpResponseMessage response = new HttpResponseMessage(status);
			if ((int)status >= 200 && (int)status <= 299)
			{
				response.Content = new StringContent(body, Encoding.UTF8, "application/json");
			}
			return response;
		}
	}
}
