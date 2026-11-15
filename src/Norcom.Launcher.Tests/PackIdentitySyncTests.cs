using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PackIdentitySyncTests
{
	[Fact]
	public async Task SameServerPinWithAChangedPackInstallsTheClientMod()
	{
		PinFetch.ResetForTests();
		string? previous = Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
		string home = Path.Combine(Path.GetTempPath(), "norcom-identity-" + Guid.NewGuid().ToString("n"));
		Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", home);
		string game = Path.Combine(home, "game");
		string cache = Path.Combine(home, "cache");
		string managed = Path.Combine(home, "managed-files.json");
		Directory.CreateDirectory(game);
		Directory.CreateDirectory(Path.Combine(game, "saves", "world"));
		Directory.CreateDirectory(Path.Combine(game, "screenshots"));
		await File.WriteAllTextAsync(Path.Combine(game, "saves", "world", "level.dat"), "world");
		await File.WriteAllTextAsync(Path.Combine(game, "options.txt"), "fov:0.7");
		await File.WriteAllTextAsync(Path.Combine(game, "screenshots", "shot.png"), "png");
		await File.WriteAllTextAsync(Path.Combine(game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		byte[] xaero = Encoding.UTF8.GetBytes("xaero-bytes");
		PackHost host = new PackHost(PinGate.LivePin);
		host.Set(PackOf("1.0.44", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both")));
		try
		{
			using HttpClient http = new HttpClient(host);
			await new PackSyncService(http).SyncAsync(game, managed, cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
			Assert.Equal("sodium-bytes", await File.ReadAllTextAsync(Path.Combine(game, "mods", "sodium.jar")));
			Assert.False(File.Exists(Path.Combine(game, "mods", "xaerominimap.jar")));
			host.Urls.Clear();
			host.Set(PackOf("1.0.45", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both"), Mod("xaerominimap.jar", "https://cdn.example/xaero.jar", xaero, "client")));
			await new PackSyncService(http).SyncAsync(game, managed, cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
			Assert.Equal("xaero-bytes", await File.ReadAllTextAsync(Path.Combine(game, "mods", "xaerominimap.jar")));
			Assert.Equal("sodium-bytes", await File.ReadAllTextAsync(Path.Combine(game, "mods", "sodium.jar")));
			Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(game, "saves", "world", "level.dat")));
			Assert.Equal("fov:0.7", await File.ReadAllTextAsync(Path.Combine(game, "options.txt")));
			Assert.Equal("png", await File.ReadAllTextAsync(Path.Combine(game, "screenshots", "shot.png")));
			Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(game));
			PackIdentity recorded = PackIdentity.TryRead(managed);
			Assert.NotNull(recorded);
			Assert.Equal("1.0.45", recorded.PackVersion);
			host.Urls.Clear();
			await new PackSyncService(http).SyncAsync(game, managed, cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
			Assert.DoesNotContain(host.Urls, url => url.EndsWith("index.toml", StringComparison.Ordinal) || url.Contains("xaero.jar", StringComparison.Ordinal));
			Assert.Contains("pack resync skipped", await File.ReadAllTextAsync(NorcomPaths.LogPath), StringComparison.Ordinal);
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", previous);
			PinFetch.ResetForTests();
		}
	}

	[Fact]
	public async Task MissingPackIdentitySyncsEvenWhenThePinAndJarsMatch()
	{
		using Holder holder = Holder.Open();
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		byte[] xaero = Encoding.UTF8.GetBytes("xaero-bytes");
		PackHost host = new PackHost(PinGate.LivePin);
		host.Set(PackOf("1.0.45", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both"), Mod("xaerominimap.jar", "https://cdn.example/xaero.jar", xaero, "client")));
		Directory.CreateDirectory(Path.Combine(holder.Game, "mods"));
		await File.WriteAllBytesAsync(Path.Combine(holder.Game, "mods", "sodium.jar"), sodium);
		await File.WriteAllTextAsync(Path.Combine(holder.Game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
		await File.WriteAllTextAsync(holder.Managed, System.Text.Json.JsonSerializer.Serialize(new[] { new ManagedEntry("mods/sodium.jar", false) }));
		await File.WriteAllTextAsync(Path.Combine(holder.Game, "saves", "world", "level.dat"), "world");
		using HttpClient http = new HttpClient(host);
		await new PackSyncService(http).SyncAsync(holder.Game, holder.Managed, holder.Cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
		Assert.Equal("xaero-bytes", await File.ReadAllTextAsync(Path.Combine(holder.Game, "mods", "xaerominimap.jar")));
		Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(holder.Game, "saves", "world", "level.dat")));
		Assert.Equal("1.0.45", PackIdentity.TryRead(holder.Managed).PackVersion);
	}

	[Fact]
	public async Task ClientModRemovedThenAddedIsInstalledAgain()
	{
		using Holder holder = Holder.Open();
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		byte[] xaero = Encoding.UTF8.GetBytes("xaero-bytes");
		PackHost host = new PackHost(PinGate.LivePin);
		ModFile sodiumMod = Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both");
		ModFile xaeroMod = Mod("xaerominimap.jar", "https://cdn.example/xaero.jar", xaero, "client");
		host.Set(PackOf("1.0.44", sodiumMod, xaeroMod));
		using HttpClient http = new HttpClient(host);
		await Sync(http, holder);
		Assert.Equal("xaero-bytes", await File.ReadAllTextAsync(Path.Combine(holder.Game, "mods", "xaerominimap.jar")));
		host.Set(PackOf("1.0.45", sodiumMod));
		await Sync(http, holder);
		Assert.False(File.Exists(Path.Combine(holder.Game, "mods", "xaerominimap.jar")));
		host.Set(PackOf("1.0.46", sodiumMod, xaeroMod));
		await Sync(http, holder);
		Assert.Equal("xaero-bytes", await File.ReadAllTextAsync(Path.Combine(holder.Game, "mods", "xaerominimap.jar")));
		Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(holder.Game, "saves", "world", "level.dat")));
		Assert.Equal("1.0.46", PackIdentity.TryRead(holder.Managed).PackVersion);
	}

	[Fact]
	public async Task RepairResyncsAMatchingPackAndLeavesPlayerFiles()
	{
		using Holder holder = Holder.Open();
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		PackHost host = new PackHost(PinGate.LivePin);
		host.Set(PackOf("1.0.45", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both")));
		using HttpClient http = new HttpClient(host);
		await Sync(http, holder);
		await File.WriteAllTextAsync(Path.Combine(holder.Cache, "stale.txt"), "stale");
		await File.WriteAllTextAsync(Path.Combine(holder.Game, "mods", "sodium.jar"), "corrupt");
		await File.WriteAllTextAsync(Path.Combine(holder.Game, "mods", "extra.jar"), "extra");
		await File.WriteAllTextAsync(Path.Combine(holder.Game, "keep-me.txt"), "notes");
		List<ManagedEntry> entries = System.Text.Json.JsonSerializer.Deserialize<List<ManagedEntry>>(await File.ReadAllTextAsync(holder.Managed)) ?? new List<ManagedEntry>();
		entries.Add(new ManagedEntry("mods/extra.jar", false));
		entries.Add(new ManagedEntry("saves/world/level.dat", false));
		await File.WriteAllTextAsync(holder.Managed, System.Text.Json.JsonSerializer.Serialize(entries));
		host.Urls.Clear();
		await new PackSyncService(http).SyncAsync(holder.Game, holder.Managed, holder.Cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None, force: true);
		Assert.False(File.Exists(Path.Combine(holder.Cache, "stale.txt")));
		Assert.Contains(host.Urls, url => url.EndsWith("index.toml", StringComparison.Ordinal));
		Assert.Equal("sodium-bytes", await File.ReadAllTextAsync(Path.Combine(holder.Game, "mods", "sodium.jar")));
		Assert.False(File.Exists(Path.Combine(holder.Game, "mods", "extra.jar")));
		Assert.Equal("world", await File.ReadAllTextAsync(Path.Combine(holder.Game, "saves", "world", "level.dat")));
		Assert.Equal("fov:0.7", await File.ReadAllTextAsync(Path.Combine(holder.Game, "options.txt")));
		Assert.Equal("png", await File.ReadAllTextAsync(Path.Combine(holder.Game, "screenshots", "shot.png")));
		Assert.Equal("notes", await File.ReadAllTextAsync(Path.Combine(holder.Game, "keep-me.txt")));
		string manifest = await File.ReadAllTextAsync(holder.Managed);
		Assert.Contains("sodium.jar", manifest, StringComparison.Ordinal);
		Assert.DoesNotContain("extra.jar", manifest, StringComparison.Ordinal);
		Assert.Equal("1.0.45", PackIdentity.TryRead(holder.Managed).PackVersion);
	}

	[Fact]
	public async Task FailedSyncDoesNotRecordTheNewPackIdentity()
	{
		using Holder holder = Holder.Open();
		byte[] sodium = Encoding.UTF8.GetBytes("sodium-bytes");
		byte[] xaero = Encoding.UTF8.GetBytes("xaero-bytes");
		PackHost host = new PackHost(PinGate.LivePin);
		host.Set(PackOf("1.0.44", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both")));
		using HttpClient http = new HttpClient(host);
		await Sync(http, holder);
		Assert.Equal("1.0.44", PackIdentity.TryRead(holder.Managed).PackVersion);
		host.CorruptJars = true;
		host.Set(PackOf("1.0.45", Mod("sodium.jar", "https://cdn.example/sodium.jar", sodium, "both"), Mod("xaerominimap.jar", "https://cdn.example/xaero.jar", xaero, "client")));
		string prism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher", "instances", "NORCOM");
		Directory.CreateDirectory(prism);
		string cfg = Path.Combine(prism, "instance.cfg");
		bool existed = File.Exists(cfg);
		if (!existed)
		{
			await File.WriteAllTextAsync(cfg, "[General]\nname=NORCOM\n");
		}
		try
		{
			await Assert.ThrowsAsync<PinMismatchException>(() => Sync(http, holder));
			Assert.Equal("1.0.44", PackIdentity.TryRead(holder.Managed).PackVersion);
			Assert.False(File.Exists(Path.Combine(holder.Game, "mods", "xaerominimap.jar")));
		}
		finally
		{
			if (!existed && File.Exists(cfg))
			{
				File.Delete(cfg);
			}
		}
	}

	[Fact]
	public async Task OfflineFeedDoesNotBlockAnInstalledClient()
	{
		using Holder holder = Holder.Open();
		using HttpClient http = new HttpClient(new OfflineHandler());
		PlayOutcome installed = await PlayConductor.MinecraftAsync(new MinecraftPlayRequest
		{
			LockPath = Path.Combine(holder.Home, "installed.lock"),
			ExistingInstall = true,
			EnsureJava = _ => Task.FromResult("java"),
			SyncPack = token => new PackSyncService(http).SyncAsync(holder.Game, holder.Managed, holder.Cache, "https://pack.example/pack.toml", new TransferMeter(), token),
			PreparePrism = _ => new PrismPreparation { CanStart = true, Executable = "prism.exe", Arguments = Array.Empty<string>(), Message = PrismMessages.Opening },
			RunPrism = (prepared, _) => Task.FromResult(prepared)
		}, CancellationToken.None);
		Assert.True(installed.Started);
		Assert.Contains(PlayConductor.PackSkipped, installed.Message, StringComparison.Ordinal);
		Assert.Contains(PackSyncService.UnreachableLog, await File.ReadAllTextAsync(NorcomPaths.LogPath), StringComparison.Ordinal);
		Assert.Null(PackIdentity.TryRead(holder.Managed));
		PlayOutcome fresh = await PlayConductor.MinecraftAsync(new MinecraftPlayRequest
		{
			LockPath = Path.Combine(holder.Home, "fresh.lock"),
			ExistingInstall = false,
			EnsureJava = _ => Task.FromResult("java"),
			SyncPack = token => new PackSyncService(http).SyncAsync(holder.Game, holder.Managed, holder.Cache, "https://pack.example/pack.toml", new TransferMeter(), token),
			PreparePrism = _ => new PrismPreparation { CanStart = true, Executable = "prism.exe", Arguments = Array.Empty<string>(), Message = PrismMessages.Opening },
			RunPrism = (prepared, _) => Task.FromResult(prepared)
		}, CancellationToken.None);
		Assert.False(fresh.Started);
		Assert.Contains(PlayConductor.PackFailed, fresh.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void StartupAndRepairCallPackSync()
	{
		string shell = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Norcom.Launcher", "Norcom.Launcher.ViewModels", "ShellViewModel.cs"));
		string play = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Norcom.Launcher.Core", "Norcom", "Launcher", "Core", "Games", "Minecraft", "MinecraftGame.cs"));
		Assert.Contains("SyncPackOnStartupAsync", shell, StringComparison.Ordinal);
		Assert.Contains("force: true", shell, StringComparison.Ordinal);
		Assert.Contains("PackSyncService", play, StringComparison.Ordinal);
	}

	private static Task Sync(HttpClient http, Holder holder)
	{
		return new PackSyncService(http).SyncAsync(holder.Game, holder.Managed, holder.Cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
	}

	private sealed class Holder : IDisposable
	{
		public string Home { get; }

		public string Game { get; }

		public string Cache { get; }

		public string Managed { get; }

		private readonly string? _previous;

		private Holder(string home, string? previous)
		{
			Home = home;
			Game = Path.Combine(home, "game");
			Cache = Path.Combine(home, "cache");
			Managed = Path.Combine(home, "managed-files.json");
			_previous = previous;
		}

		public static Holder Open()
		{
			PinFetch.ResetForTests();
			string? previous = Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
			string home = Path.Combine(Path.GetTempPath(), "norcom-identity-" + Guid.NewGuid().ToString("n"));
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", home);
			Holder holder = new Holder(home, previous);
			Directory.CreateDirectory(holder.Game);
			Directory.CreateDirectory(Path.Combine(holder.Game, "saves", "world"));
			Directory.CreateDirectory(Path.Combine(holder.Game, "screenshots"));
			File.WriteAllText(Path.Combine(holder.Game, "saves", "world", "level.dat"), "world");
			File.WriteAllText(Path.Combine(holder.Game, "options.txt"), "fov:0.7");
			File.WriteAllText(Path.Combine(holder.Game, "screenshots", "shot.png"), "png");
			File.WriteAllText(Path.Combine(holder.Game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
			return holder;
		}

		public void Dispose()
		{
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", _previous);
			PinFetch.ResetForTests();
		}
	}

	private static string RepoRoot()
	{
		string dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			if (Directory.Exists(Path.Combine(dir, "src", "Norcom.Launcher.Core")))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir) ?? "";
		}
		throw new DirectoryNotFoundException("norcom-pack");
	}

	private static ModFile Mod(string file, string url, byte[] body, string side)
	{
		return new ModFile(file, url, body, side);
	}

	private static PackSet PackOf(string version, params ModFile[] mods)
	{
		return new PackSet(version, mods);
	}

	private sealed record ModFile(string File, string Url, byte[] Body, string Side);

	private sealed record PackSet(string Version, ModFile[] Mods);

	private sealed class OfflineHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			throw new HttpRequestException("offline");
		}
	}

	private sealed class PackHost : HttpMessageHandler
	{
		private readonly string _pin;
		private PackSet _pack = new PackSet("0", Array.Empty<ModFile>());
		private string _packText = "";
		private byte[] _index = Array.Empty<byte>();
		private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

		public PackHost(string pin)
		{
			_pin = pin;
		}

		public List<string> Urls { get; } = new List<string>();

		public bool CorruptJars { get; set; }

		public void Set(PackSet pack)
		{
			_pack = pack;
			_files.Clear();
			StringBuilder index = new StringBuilder();
			index.Append("hash-format = \"sha256\"\n");
			foreach (ModFile mod in pack.Mods)
			{
				string toml = "filename = \"" + mod.File + "\"\nside = \"" + mod.Side + "\"\n[download]\nurl = \"" + mod.Url + "\"\nhash-format = \"sha256\"\nhash = \"" + Hex(mod.Body) + "\"\n";
				byte[] meta = Encoding.UTF8.GetBytes(toml);
				string rel = "mods/" + Path.GetFileNameWithoutExtension(mod.File) + ".pw.toml";
				index.Append("[[files]]\nfile = \"").Append(rel).Append("\"\nhash = \"").Append(Hex(meta)).Append("\"\n");
				_files[rel] = meta;
				_files[mod.Url] = mod.Body;
			}
			_index = Encoding.UTF8.GetBytes(index.ToString());
			_packText = "name = \"NORCOM\"\nversion = \"" + pack.Version + "\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + Hex(_index) + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
		}

		public string Version => _pack.Version;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			Urls.Add(url);
			if (url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
			{
				string pin = "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + _pin + "\",\"jars\":[]}";
				return Task.FromResult(Text(pin));
			}
			if (url.EndsWith("pack.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Text(_packText));
			}
			if (url.EndsWith("index.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(Bytes(_index));
			}
			foreach (KeyValuePair<string, byte[]> file in _files)
			{
				if (url.EndsWith(file.Key, StringComparison.Ordinal) || url.Contains(file.Key, StringComparison.Ordinal))
				{
					byte[] body = file.Value;
					if (CorruptJars && url.Contains("://", StringComparison.Ordinal) && !url.Contains(".pw.toml", StringComparison.Ordinal))
					{
						body = body.Concat(new byte[] { 1 }).ToArray();
					}
					return Task.FromResult(Bytes(body));
				}
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

		private static string Hex(byte[] bytes)
		{
			return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		}
	}
}
