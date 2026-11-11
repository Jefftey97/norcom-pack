using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class RamPendingTests
{
	[Fact]
	public void MissingPrismKeepsRamPendingUntilTheNextPlayWritesIt()
	{
		string root = NewDir();
		string game = Path.Combine(root, ".norcom");
		string data = Path.Combine(root, "prism");
		string settingsPath = Path.Combine(root, "settings.json");
		Directory.CreateDirectory(game);
		LauncherSettings settings = new LauncherSettings { RamMb = 8192, RamSpecified = true, RamPending = true };
		settings.Save(settingsPath);
		PrismPreparation missing = PrismPlay.Prepare(null, Array.Empty<string>(), data, game, "java", settings.RamMb, settings.RamPending);
		Remember(settings, settingsPath, missing.Applied);
		LauncherSettings kept = LauncherSettings.Load(settingsPath);
		Assert.False(missing.CanStart);
		Assert.False(missing.Applied);
		Assert.True(kept.RamPending);
		string prism = Path.Combine(root, "prismlauncher.exe");
		File.WriteAllText(prism, "prism");
		PrismPreparation applied = PrismPlay.Prepare(prism, Array.Empty<string>(), data, game, "java", kept.RamMb, kept.RamPending);
		Remember(kept, settingsPath, applied.Applied);
		LauncherSettings done = LauncherSettings.Load(settingsPath);
		Assert.True(applied.Applied);
		Assert.False(done.RamPending);
		string cfg = File.ReadAllText(Path.Combine(data, "instances", "NORCOM", "instance.cfg"));
		Assert.Contains("MaxMemAlloc=8192", cfg, StringComparison.Ordinal);
	}

	[Fact]
	public void MissingSteamKeepsZomboidRamPendingUntilTheNextPlayWritesIt()
	{
		string root = NewDir();
		string user = Path.Combine(root, "user");
		string settingsPath = Path.Combine(root, "settings.json");
		LauncherSettings settings = new LauncherSettings { ZomboidRamMb = 8192, ZomboidRamSpecified = true, ZomboidRamPending = true };
		settings.Save(settingsPath);
		ZomboidLaunchPlan missing = ZomboidLaunch.Prepare(EmptySteam(), user, root, "", "", "", settings.ZomboidRamMb);
		RememberZomboid(settings, settingsPath, missing.Applied);
		LauncherSettings kept = LauncherSettings.Load(settingsPath);
		Assert.False(missing.CanStart);
		Assert.False(missing.Applied);
		Assert.True(kept.ZomboidRamPending);
		Assert.False(File.Exists(Path.Combine(user, "Zomboid", ZomboidMemory.FileName)));
		ZomboidLaunchPlan applied = ZomboidLaunch.Prepare(FoundSteam(root), user, root, "", "{}", "", kept.ZomboidRamMb);
		RememberZomboid(kept, settingsPath, applied.Applied);
		LauncherSettings done = LauncherSettings.Load(settingsPath);
		Assert.True(applied.Applied);
		Assert.False(done.ZomboidRamPending);
		Assert.Contains("\"-Xmx8192m\"", File.ReadAllText(Path.Combine(user, "Zomboid", ZomboidMemory.FileName)), StringComparison.Ordinal);
	}

	private static void Remember(LauncherSettings settings, string path, bool applied)
	{
		if (applied && settings.RamPending)
		{
			settings.RamPending = false;
			settings.Save(path);
		}
	}

	private static void RememberZomboid(LauncherSettings settings, string path, bool applied)
	{
		if (applied && settings.ZomboidRamPending)
		{
			settings.ZomboidRamPending = false;
			settings.Save(path);
		}
	}

	private static SteamSources EmptySteam()
	{
		return new SteamSources
		{
			RegistryPaths = Array.Empty<string>(),
			FileExists = _ => false,
			DirectoryExists = _ => false,
			ReadText = _ => null
		};
	}

	private static SteamSources FoundSteam(string root)
	{
		string steam = Path.Combine(root, "steam");
		string game = Path.Combine(steam, "steamapps", "common", "ProjectZomboid");
		Directory.CreateDirectory(game);
		string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[vdf] = "\"path\" \"" + steam.Replace("\\", "\\\\") + "\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game };
		return new SteamSources
		{
			RegistryPaths = new[] { steam },
			FileExists = path => files.ContainsKey(path),
			DirectoryExists = path => dirs.Contains(path),
			ReadText = path => files.TryGetValue(path, out string text) ? text : null
		};
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-ram-pending-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
