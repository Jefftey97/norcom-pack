using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ZomboidLaunchTests
{
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
			Assert.Contains("language=EN", ini);
			Assert.Contains("frameRate=60", ini);
			Assert.True(File.Exists(Path.Combine(user, "Zomboid", "options.ini.norcom-backup")));
			SteamStart.Open(plan.Url);
			Assert.Equal(plan.Url, opened);
		}
		finally
		{
			SteamStart.OpenUrl = previous;
		}
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

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-zomboid-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
