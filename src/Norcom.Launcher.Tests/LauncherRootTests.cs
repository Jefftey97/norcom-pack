using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class LauncherRootTests
{
	[Fact]
	public void InstallDirectoryIsThePrimaryNorcomLauncherFolder()
	{
		string install = FriendInstaller.InstallDirectory;
		Assert.Equal(Path.Combine(NorcomPaths.NorcomHome, "Launcher"), install);
		Assert.EndsWith(Path.Combine("NORCOM", "Launcher"), install);
		Assert.False(Path.GetFileName(install).Equals("NorcomLauncher", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(Path.Combine(install, "NorcomLauncher.exe"), FriendInstaller.InstalledExePath);
	}

	[Fact]
	public void UpdateFromTheAltRootTargetsPrimaryAndLeavesDesktopCopiesAlone()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-root-" + Guid.NewGuid().ToString("n"));
		string local = Path.Combine(root, "Local");
		string primary = Path.Combine(local, "NORCOM", "Launcher", "NorcomLauncher.exe");
		string alt = Path.Combine(local, "NorcomLauncher", "NorcomLauncher.exe");
		string desktop = Path.Combine(root, "Desktop", "NORCOM-WPF-Test", "run42", "NorcomLauncher.exe");
		Assert.Equal(Path.GetFullPath(alt), LauncherInstall.AltExeFor(primary));
		Assert.True(LauncherInstall.IsAltInstall(alt, primary));
		Assert.True(LauncherInstall.ShouldPromoteAlt(alt, primary));
		Assert.False(LauncherInstall.ShouldPromoteAlt(primary, primary));
		Assert.Equal(Path.GetFullPath(primary), LauncherInstall.Resolve(alt, primary));
		Assert.Equal(Path.GetFullPath(primary), LauncherInstall.Resolve(primary, primary));
		Assert.Equal(Path.GetFullPath(desktop), LauncherInstall.Resolve(desktop, primary));
		Assert.Equal("", LauncherInstall.AltExeFor(desktop));
	}

	[Fact]
	public void AppliedShaDoesNotHideAStaleRunningCopyOrAStalePrimary()
	{
		string sha = new string('a', 64);
		Assert.False(UpdateReport.SkipApplied(sha, sha, "2.0.40", "2.0.41", "2.0.41", false));
		Assert.False(UpdateReport.SkipApplied(sha, sha, "2.0.41", "2.0.41", "2.0.40", true));
		Assert.True(UpdateReport.SkipApplied(sha, sha, "2.0.41", "2.0.41", "2.0.41", false));
		Assert.True(UpdateReport.SkipApplied(sha, sha, "2.0.41", "2.0.41", "2.0.41", true));
		Assert.False(UpdateReport.SkipApplied(sha, new string('b', 64), "2.0.41", "2.0.41", "2.0.41", false));
		Assert.True(LauncherInstall.ShouldCopyRunningToPrimary(true, "2.0.42", null, "2.0.42"));
		Assert.True(LauncherInstall.ShouldCopyRunningToPrimary(true, "2.0.42", "2.0.40", "2.0.42"));
		Assert.False(LauncherInstall.ShouldCopyRunningToPrimary(true, "2.0.40", "2.0.40", "2.0.42"));
		Assert.False(LauncherInstall.ShouldCopyRunningToPrimary(false, "2.0.42", "2.0.40", "2.0.42"));
		Assert.False(LauncherInstall.ShouldCopyRunningToPrimary(true, "2.0.42", "2.0.42", "2.0.42"));
	}

	[Fact]
	public void InstallLandsOnPrimaryAndSyncsAnExistingAltCopy()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-install-" + Guid.NewGuid().ToString("n"));
		string local = Path.Combine(root, "Local");
		string primaryDir = Path.Combine(local, "NORCOM", "Launcher");
		string primary = Path.Combine(primaryDir, "NorcomLauncher.exe");
		string altDir = Path.Combine(local, "NorcomLauncher");
		string alt = Path.Combine(altDir, "NorcomLauncher.exe");
		Directory.CreateDirectory(altDir);
		File.WriteAllText(alt, "old-2.0.40");
		string sourceDir = Path.Combine(root, "bundle");
		Directory.CreateDirectory(sourceDir);
		string source = Path.Combine(sourceDir, "NorcomLauncher.exe");
		File.WriteAllText(source, "new-2.0.42");
		FriendInstaller.InstallLauncher(source, primaryDir);
		Assert.Equal("new-2.0.42", File.ReadAllText(primary));
		Assert.Equal("new-2.0.42", File.ReadAllText(alt));
		Assert.Equal(NorcomVersion.Current, File.ReadAllText(Path.Combine(primaryDir, "payload-version.txt")).Trim());
		Assert.Equal(NorcomVersion.Current, File.ReadAllText(Path.Combine(altDir, "payload-version.txt")).Trim());
		Assert.False(LauncherInstall.TrySyncAlt(primary));
		string elsewhere = Path.Combine(root, "Downloads");
		Directory.CreateDirectory(elsewhere);
		FriendInstaller.InstallLauncher(source, elsewhere);
		Assert.Equal("new-2.0.42", File.ReadAllText(Path.Combine(elsewhere, "NorcomLauncher.exe")));
		Assert.Equal("new-2.0.42", File.ReadAllText(alt));
	}

	[Fact]
	public void ShortcutsPointAtPrimaryWhenTheyTargetTheAltLauncher()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-lnk-" + Guid.NewGuid().ToString("n"));
		string primary = Path.Combine(root, "Local", "NORCOM", "Launcher", "NorcomLauncher.exe");
		string alt = Path.Combine(root, "Local", "NorcomLauncher", "NorcomLauncher.exe");
		Assert.True(ShortcutTargets.ShouldRetarget(alt, primary, anyLauncherExe: true));
		Assert.True(ShortcutTargets.ShouldRetarget("C:\\Old\\NORCOM-Launcher.ps1", primary, anyLauncherExe: true));
		Assert.True(ShortcutTargets.ShouldRetarget("C:\\Old\\NORCOM Launcher.exe", primary, anyLauncherExe: false));
		Assert.False(ShortcutTargets.ShouldRetarget(primary, primary, anyLauncherExe: true));
		Assert.False(ShortcutTargets.ShouldRetarget(alt, primary, anyLauncherExe: false));
		Assert.False(ShortcutTargets.ShouldRetarget("C:\\Games\\notepad.exe", primary, anyLauncherExe: true));
	}

	[Fact]
	public void ApplyScriptUpdatesPrimaryAndTheAltCopyWithoutDeletingTheInstall()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-script-" + Guid.NewGuid().ToString("n"));
		string staged = Path.Combine(root, "norcom-launcher-upd-abc", "extract");
		Directory.CreateDirectory(staged);
		string payload = Path.Combine(staged, "payload-version.txt");
		File.WriteAllText(payload, "2.0.42\n");
		string newExe = Path.Combine(staged, "NorcomLauncher.exe");
		string primary = Path.Combine(root, "Local", "NORCOM", "Launcher", "NorcomLauncher.exe");
		string alt = Path.Combine(root, "Local", "NorcomLauncher", "NorcomLauncher.exe");
		string script = SelfUpdateService.BuildApplyScript(newExe, primary, 10, "2.0.42", Path.Combine(root, "bak", "NorcomLauncher.previous.exe"), Path.Combine(root, "launcher.log"), Path.Combine(root, "update-status.txt"), Path.Combine(root, "update-applied.sha"), new string('a', 64), false, alt, payload);
		Assert.Contains("copy /Y \"" + newExe + "\" \"" + primary + "\"", script);
		Assert.Contains("copy /Y \"" + newExe + "\" \"" + alt + "\"", script);
		Assert.Contains("copy /Y \"" + payload + "\"", script);
		Assert.Contains("start \"\" \"" + primary + "\"", script);
		Assert.DoesNotContain("rmdir /s /q \"" + Path.Combine(root, "Local", "NORCOM"), script);
		string promote = LauncherInstall.BuildPromoteScript(10, newExe, primary, alt);
		Assert.Contains("start \"\" \"" + primary + "\"", promote);
		Assert.Contains("copy /Y \"" + newExe + "\" \"" + alt + "\"", promote);
		Assert.DoesNotContain(".norcom", promote);
	}
}
