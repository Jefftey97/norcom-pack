using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class LauncherCleanupTests
{
	[Fact]
	public void OrdinaryCleanupPreservesRecoveryWhileRemovingObsoletePayloads()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-clean-" + Guid.NewGuid().ToString("n"));
		string home = Path.Combine(root, "NORCOM");
		string launcher = Path.Combine(home, "Launcher");
		string game = Path.Combine(root, "AppData", ".norcom");
		string temp = Path.Combine(root, "Temp");
		string desktop = Path.Combine(root, "Desktop");
		Directory.CreateDirectory(launcher);
		string live = Path.Combine(launcher, "NorcomLauncher.exe");
		File.WriteAllText(live, "live");
		File.WriteAllText(Path.Combine(launcher, "launcher.log"), "keep");
		File.WriteAllText(Path.Combine(launcher, "NORCOM-Launcher.ps1"), "old");
		File.WriteAllText(Path.Combine(home, "settings.json"), "{\"CloseToTray\":false}");
		Directory.CreateDirectory(Path.Combine(home, "jre", "bin"));
		File.WriteAllText(Path.Combine(home, "jre", "bin", "java.exe"), "jre");
		string previous = Path.Combine(home, "recovery", "self-update-2.0.29");
		Directory.CreateDirectory(previous);
		File.WriteAllText(Path.Combine(previous, "NorcomLauncher.previous.exe"), "old-exe");
		string retired = Path.Combine(home, "recovery", "install-20260101");
		Directory.CreateDirectory(Path.Combine(retired, "files"));
		File.WriteAllText(Path.Combine(retired, "files", "NORCOM Launcher.exe"), "legacy");
		string pack = Path.Combine(home, "recovery", "pack-20260101");
		Directory.CreateDirectory(pack);
		File.WriteAllText(Path.Combine(pack, "mods.jar"), "pack");
		Directory.CreateDirectory(Path.Combine(game, "saves", "world"));
		File.WriteAllText(Path.Combine(game, "saves", "world", "level.dat"), "save");
		string upd = Path.Combine(temp, "norcom-launcher-upd-abc", "extract");
		Directory.CreateDirectory(upd);
		File.WriteAllText(Path.Combine(upd, "NorcomLauncher.exe"), "payload");
		File.WriteAllText(Path.Combine(temp, "norcom-apply-abc.cmd"), "cmd");
		File.WriteAllText(Path.Combine(temp, "notes.txt"), "keep");
		string run42 = Path.Combine(desktop, "NORCOM-WPF-Test", "run42-close-exits");
		Directory.CreateDirectory(run42);
		File.WriteAllText(Path.Combine(run42, "NorcomLauncher.exe"), "desktop-test");
		IReadOnlyList<string> removed = LauncherCleanup.RemoveObsolete(live, home, game, temp);
		Assert.DoesNotContain(removed, path => path.Contains("self-update-2.0.29", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(removed, path => path.Contains("install-20260101", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(removed, path => path.EndsWith("NORCOM-Launcher.ps1", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(removed, path => path.Contains("norcom-launcher-upd-abc", StringComparison.OrdinalIgnoreCase));
		Assert.Equal("old-exe", File.ReadAllText(Path.Combine(previous, "NorcomLauncher.previous.exe")));
		Assert.Equal("legacy", File.ReadAllText(Path.Combine(retired, "files", "NORCOM Launcher.exe")));
		Assert.False(Directory.Exists(Path.Combine(temp, "norcom-launcher-upd-abc")));
		Assert.False(File.Exists(Path.Combine(temp, "norcom-apply-abc.cmd")));
		Assert.False(File.Exists(Path.Combine(launcher, "NORCOM-Launcher.ps1")));
		Assert.Equal("live", File.ReadAllText(live));
		Assert.Equal("keep", File.ReadAllText(Path.Combine(launcher, "launcher.log")));
		Assert.Equal("{\"CloseToTray\":false}", File.ReadAllText(Path.Combine(home, "settings.json")));
		Assert.Equal("jre", File.ReadAllText(Path.Combine(home, "jre", "bin", "java.exe")));
		Assert.Equal("pack", File.ReadAllText(Path.Combine(pack, "mods.jar")));
		Assert.Equal("save", File.ReadAllText(Path.Combine(game, "saves", "world", "level.dat")));
		Assert.Equal("keep", File.ReadAllText(Path.Combine(temp, "notes.txt")));
		Assert.Equal("desktop-test", File.ReadAllText(Path.Combine(run42, "NorcomLauncher.exe")));
		string adminRun = Path.Combine(desktop, "run32-admin-snappy");
		Directory.CreateDirectory(adminRun);
		File.WriteAllText(Path.Combine(adminRun, "NorcomLauncher.exe"), "admin-build");
		File.WriteAllText(Path.Combine(adminRun, "NORCOM-Launcher.ps1"), "keep-admin");
		string adminRecovery = Path.Combine(adminRun, "recovery", "self-update-old");
		Directory.CreateDirectory(adminRecovery);
		File.WriteAllText(Path.Combine(adminRecovery, "NorcomLauncher.previous.exe"), "admin-old");
		LauncherCleanup.RemoveObsolete(Path.Combine(adminRun, "NorcomLauncher.exe"), adminRun, game, temp);
		Assert.Equal("admin-build", File.ReadAllText(Path.Combine(adminRun, "NorcomLauncher.exe")));
		Assert.Equal("keep-admin", File.ReadAllText(Path.Combine(adminRun, "NORCOM-Launcher.ps1")));
		Assert.Equal("admin-old", File.ReadAllText(Path.Combine(adminRecovery, "NorcomLauncher.previous.exe")));
		Directory.CreateDirectory(Path.Combine(home, "NorcomAdmin"));
		File.WriteAllText(Path.Combine(home, "NorcomAdmin", "NorcomAdmin.exe"), "admin");
		Directory.CreateDirectory(Path.Combine(home, "CrashReceiver"));
		File.WriteAllText(Path.Combine(home, "CrashReceiver", "CrashReceiver.exe"), "crash");
		Directory.CreateDirectory(Path.Combine(home, "java"));
		File.WriteAllText(Path.Combine(home, "java", "java.exe"), "server-java");
		Directory.CreateDirectory(Path.Combine(game, "mods"));
		File.WriteAllText(Path.Combine(game, "mods", "example.jar"), "mod");
		string hiddenOld = Path.Combine(game, "recovery", "self-update-hidden");
		Directory.CreateDirectory(hiddenOld);
		File.WriteAllText(Path.Combine(hiddenOld, "NorcomLauncher.previous.exe"), "nope");
		LauncherCleanup.RemoveObsolete(live, home, game, temp);
		Assert.Equal("admin", File.ReadAllText(Path.Combine(home, "NorcomAdmin", "NorcomAdmin.exe")));
		Assert.Equal("crash", File.ReadAllText(Path.Combine(home, "CrashReceiver", "CrashReceiver.exe")));
		Assert.Equal("server-java", File.ReadAllText(Path.Combine(home, "java", "java.exe")));
		Assert.Equal("mod", File.ReadAllText(Path.Combine(game, "mods", "example.jar")));
		Assert.Equal("nope", File.ReadAllText(Path.Combine(hiddenOld, "NorcomLauncher.previous.exe")));
		string canonical = Path.GetFullPath(live);
		string stale = Path.Combine(previous, "NorcomLauncher.previous.exe");
		Assert.Equal(canonical, LauncherInstall.Resolve(stale, live));
		Assert.Equal(canonical, LauncherInstall.Resolve(live, live));
		Assert.Equal(Path.GetFullPath(Path.Combine(run42, "NorcomLauncher.exe")), LauncherInstall.Resolve(Path.Combine(run42, "NorcomLauncher.exe"), live));
	}

	[Fact]
	public void ApplyScriptDeletesOnlyTheUpdatePayloadFolder()
	{
		string payload = Path.Combine(Path.GetTempPath(), "norcom-launcher-upd-abc", "extract", "NorcomLauncher.exe");
		string script = SelfUpdateService.BuildApplyScript(payload, "C:\\Users\\SkaAh\\AppData\\Local\\NORCOM\\Launcher\\NorcomLauncher.exe", 10, "2.0.30", "C:\\bak\\NorcomLauncher.previous.exe", "C:\\log.txt", "C:\\status.txt", "C:\\applied.sha", new string('a', 64));
		Assert.Contains("rmdir /s /q \"" + LauncherCleanup.UpdatePayloadDirectory(payload) + "\"", script);
		Assert.DoesNotContain("rmdir /s /q \"C:\\Users\\SkaAh\\AppData\\Local\\NORCOM", script);
		Assert.Null(LauncherCleanup.UpdatePayloadDirectory("C:\\Users\\SkaAh\\AppData\\Local\\NORCOM\\Launcher\\NorcomLauncher.exe"));
	}
}
