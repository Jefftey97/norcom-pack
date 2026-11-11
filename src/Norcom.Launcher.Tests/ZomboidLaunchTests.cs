using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ZomboidLaunchTests
{
	[Fact]
	public void ConnectLinkStartsSteamApplaunchWithTheConnectArguments()
	{
		const string steam = @"C:\Program Files (x86)\Steam";
		string exe = Path.Combine(steam, "steam.exe");
		bool ok = SteamAppLaunch.TryBuild("steam://rungameid/108600//+connect%2069.9.178.16:27758", new[] { steam }, path => path == exe, out string steamExe, out IReadOnlyList<string> args);
		Assert.True(ok);
		Assert.Equal(exe, steamExe);
		Assert.Equal(new[] { "-applaunch", "108600", "+connect", "69.9.178.16:27758" }, args);
	}

	[Fact]
	public void ConnectLinkKeepsPasswordAndHeapArguments()
	{
		const string steam = @"C:\Steam";
		bool ok = SteamAppLaunch.TryBuild("steam://rungameid/108600//+connect%20play.example.com:16261%20+password%20pw%20-Xmx4096m", new[] { steam }, _ => true, out _, out IReadOnlyList<string> args);
		Assert.True(ok);
		Assert.Equal(new[] { "-applaunch", "108600", "+connect", "play.example.com:16261", "+password", "pw", "-Xmx4096m" }, args);
	}

	[Fact]
	public void PlainLinksAndMissingSteamKeepTheSteamProtocol()
	{
		Assert.False(SteamAppLaunch.TryBuild("steam://rungameid/108600", new[] { @"C:\Steam" }, _ => true, out _, out _));
		Assert.False(SteamAppLaunch.TryBuild("steam://open/main", new[] { @"C:\Steam" }, _ => true, out _, out _));
		Assert.False(SteamAppLaunch.TryBuild("steam://rungameid/108600//+connect%20h:1", new[] { @"C:\Steam" }, _ => false, out _, out _));
		Assert.False(SteamAppLaunch.TryBuild("steam://rungameid/10x600//+connect%20h:1", new[] { @"C:\Steam" }, _ => true, out _, out _));
		Assert.False(SteamAppLaunch.TryBuild("steam://rungameid/108600//+connect%20h:1", Array.Empty<string>(), _ => true, out _, out _));
	}
	[Fact]
	public void MissingSteamIsAClearMessage()
	{
		ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(Sources(Array.Empty<string>(), new Dictionary<string, string>(), new HashSet<string>()), NewDir(), NewDir(), "");
		Assert.False(plan.CanStart);
		Assert.Equal("Steam / Project Zomboid not found", plan.Message);
		Assert.DoesNotContain("+connect", plan.Url, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void LibraryFoldersVdfFindsZomboidInASecondLibrary()
	{
		const string steam = @"C:\Program Files (x86)\Steam";
		const string library = @"D:\SteamLibrary";
		string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"108600\"\t\t\"1\"\n\t\t}\n\t}\n}\n";
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[steam + "\\steamapps\\libraryfolders.vdf"] = vdf,
			[library + "\\steamapps\\appmanifest_108600.acf"] = "\"appid\" \"108600\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			library + "\\steamapps\\common\\ProjectZomboid"
		};
		SteamGameLocation found = SteamLibrary.FindZomboid(Sources(new[] { steam }, files, dirs));
		Assert.True(found.Found);
		Assert.Equal(library + "\\steamapps\\common\\ProjectZomboid", found.GamePath);
		Assert.Equal("C:\\Program Files (x86)\\Steam", SteamLibrary.LibraryPaths(vdf)[0]);
		Assert.Equal("D:\\SteamLibrary", SteamLibrary.LibraryPaths(vdf)[1]);
	}

	[Fact]
	public void SteamWithoutZomboidIsNotFound()
	{
		const string steam = @"C:\Steam";
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[steam + "\\steamapps\\libraryfolders.vdf"] = "\"path\" \"C:\\\\Steam\"\n"
		};
		SteamGameLocation found = SteamLibrary.FindZomboid(Sources(new[] { steam }, files, new HashSet<string>()));
		Assert.False(found.Found);
		Assert.Equal(SteamLibrary.NotFound, found.Message);
	}

	[Fact]
	public void LaunchAppliesTheSavedProfileAndDoesNotAddConnect()
	{
		string home = NewDir();
		string user = NewDir();
		Directory.CreateDirectory(Path.Combine(user, "Zomboid"));
		File.WriteAllText(Path.Combine(user, "Zomboid", "options.ini"), "language=EN\nframeRate=144\n");
		GraphicsProfileStore store = new GraphicsProfileStore();
		store.Put("zomboid", new SavedGraphicsProfile { Preset = "Medium", Auto = false });
		store.Save(home);
		const string steam = "/steam";
		const string game = "/steam/steamapps/common/ProjectZomboid";
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[steam + "/steamapps/libraryfolders.vdf"] = "\"path\" \"/steam\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game };
		string opened = "";
		Action<string> previous = SteamStart.OpenUrl;
		SteamStart.OpenUrl = url => opened = url;
		try
		{
			ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(Sources(new[] { steam }, files, dirs), user, home, "");
			Assert.True(plan.CanStart);
			Assert.Equal("steam://rungameid/108600", plan.Url);
			Assert.DoesNotContain("+connect", plan.Url, StringComparison.Ordinal);
			Assert.Contains("Workshop", plan.Message);
			string ini = File.ReadAllText(Path.Combine(user, "Zomboid", "options.ini"));
			Assert.Equal("language=EN\nframeRate=144\n", ini);
			Assert.False(File.Exists(Path.Combine(user, "Zomboid", "options.ini.norcom-backup")));
			SteamStart.Open(plan.Url);
			Assert.Equal(plan.Url, opened);
		}
		finally
		{
			SteamStart.OpenUrl = previous;
		}
	}

	[Fact]
	public void FeedConnectsWithoutUsingRcon()
	{
		string json = "{\"connect\":{\"host\":\"play.example.com\",\"port\":16261,\"password\":\"from-feed\",\"rcon\":\"admin-secret\"},\"workshop\":[{\"workshopId\":\"123\",\"modId\":\"TestMod\"}]}";
		ZomboidFeedDoc feed = ZomboidFeed.Parse(json);
		string url = ZomboidFeed.SteamUrl(feed, "friend-pass", out string note);
		Assert.Equal("", note);
		Assert.Contains("+connect%20play.example.com:16261", url, StringComparison.Ordinal);
		Assert.Contains("+password%20friend-pass", url, StringComparison.Ordinal);
		Assert.DoesNotContain("from-feed", url, StringComparison.Ordinal);
		Assert.DoesNotContain("admin-secret", url, StringComparison.Ordinal);
		Assert.DoesNotContain("rcon", url, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("+password", ZomboidFeed.SteamUrl(feed, "", out _), StringComparison.Ordinal);
		string home = NewDir();
		string user = NewDir();
		ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(FoundSteam(), user, home, "", json);
		Assert.True(plan.CanStart);
		Assert.Equal(ZomboidFeed.SteamUrl(feed, "", out _), plan.Url);
		Assert.False(plan.IncludesPassword);
		ZomboidLaunchPlan withPassword = ZomboidLaunch.Prepare(FoundSteam(), user, home, "", json, "friend-pass");
		Assert.Equal(url, withPassword.Url);
		Assert.True(withPassword.IncludesPassword);
		Assert.Contains("Subscribe to these Workshop mods in Steam once: TestMod (123)", plan.Message);
		ZomboidLaunchPlan again = ZomboidLaunch.Prepare(FoundSteam(), user, home, "", json);
		Assert.DoesNotContain("Subscribe", again.Message);
	}

	[Fact]
	public async Task HashedModFileLandsInTheZomboidModsFolder()
	{
		byte[] body = Encoding.UTF8.GetBytes("mod-bytes");
		string hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
		string json = "{\"files\":[{\"path\":\"mods/LocalMap/mod.info\",\"url\":\"https://pack.example/mod.info\",\"sha256\":\"" + hash + "\"}]}";
		string user = NewDir();
		using HttpClient http = new HttpClient(new ByteHandler(body));
		ZomboidLaunchPlan plan = await ZomboidLaunch.PrepareAsync(FoundSteam(), user, NewDir(), "", json, http, CancellationToken.None);
		Assert.True(plan.CanStart);
		Assert.Equal(body, await File.ReadAllBytesAsync(Path.Combine(user, "Zomboid", "mods", "LocalMap", "mod.info")));
	}

	[Fact]
	public async Task BadHashOrEscapingPathDoesNotWrite()
	{
		string user = NewDir();
		string outside = Path.Combine(user, "escape.txt");
		await File.WriteAllTextAsync(outside, "keep");
		string json = "{\"files\":[{\"path\":\"../escape.txt\",\"url\":\"https://pack.example/x\",\"sha256\":\"" + new string('a', 64) + "\"}]}";
		using HttpClient http = new HttpClient(new ByteHandler(Encoding.UTF8.GetBytes("nope")));
		ZomboidLaunchPlan plan = await ZomboidLaunch.PrepareAsync(FoundSteam(), user, NewDir(), "", json, http, CancellationToken.None);
		Assert.False(plan.CanStart);
		Assert.Equal("keep", await File.ReadAllTextAsync(outside));
		string mismatch = "{\"files\":[{\"path\":\"mods/LocalMap/mod.info\",\"url\":\"https://pack.example/x\",\"sha256\":\"" + new string('b', 64) + "\"}]}";
		ZomboidLaunchPlan bad = await ZomboidLaunch.PrepareAsync(FoundSteam(), user, NewDir(), "", mismatch, http, CancellationToken.None);
		Assert.False(bad.CanStart);
		Assert.False(File.Exists(Path.Combine(user, "Zomboid", "mods", "LocalMap", "mod.info")));
	}

	[Fact]
	public void PreparePassesHeapOnTheSteamUrlAndWritesTheUserOverride()
	{
		string user = NewDir();
		ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(FoundSteam(), user, NewDir(), "", "{\"connect\":{\"host\":\"play.example.com\",\"port\":16261}}", "", 4096);
		Assert.True(plan.CanStart);
		Assert.Contains("+connect%20play.example.com:16261", plan.Url, StringComparison.Ordinal);
		Assert.Contains("-Xmx4096m", plan.Url, StringComparison.Ordinal);
		Assert.Contains("-Xmx4096m", File.ReadAllText(Path.Combine(user, "Zomboid", ZomboidMemory.FileName)), StringComparison.Ordinal);
		Assert.DoesNotContain("password", plan.Url, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ChosenGameRoundTrip()
	{
		string home = NewDir();
		Assert.Equal(ChosenGame.Minecraft, ChosenGame.Load(home));
		ChosenGame.Save(home, "zomboid");
		Assert.Equal(ChosenGame.Zomboid, ChosenGame.Load(home));
		ChosenGame.Save(home, "nope");
		Assert.Equal(ChosenGame.Minecraft, ChosenGame.Load(home));
	}

	private static SteamSources FoundSteam()
	{
		const string steam = "/steam";
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[steam + "/steamapps/libraryfolders.vdf"] = "\"path\" \"/steam\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam + "/steamapps/common/ProjectZomboid" };
		return Sources(new[] { steam }, files, dirs);
	}

	private static SteamSources Sources(IReadOnlyList<string> registry, Dictionary<string, string> files, HashSet<string> dirs)
	{
		return new SteamSources
		{
			RegistryPaths = registry,
			FileExists = path => files.ContainsKey(path),
			DirectoryExists = path => dirs.Contains(path),
			ReadText = path => files.TryGetValue(path, out string text) ? text : null
		};
	}

	private sealed class ByteHandler : HttpMessageHandler
	{
		private readonly byte[] _bytes;

		public ByteHandler(byte[] bytes)
		{
			_bytes = bytes;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(_bytes)
			});
		}
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-zomboid-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
