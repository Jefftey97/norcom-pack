using System;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class FriendInstaller
{
	public const string ShortcutName = "NORCOM Launcher.lnk";

	public const string ExeName = "NorcomLauncher.exe";

	public static string InstallDirectory => Path.Combine(NorcomPaths.NorcomHome, "Launcher");

	public static string InstalledExePath => Path.Combine(InstallDirectory, "NorcomLauncher.exe");

	public static string? FindBundledLauncher(string installerDirectory)
	{
		if (string.IsNullOrWhiteSpace(installerDirectory) || !Directory.Exists(installerDirectory))
		{
			return null;
		}
		string text = Path.Combine(installerDirectory, "NorcomLauncher.exe");
		if (!File.Exists(text))
		{
			return null;
		}
		return text;
	}

	public static void InstallLauncher(string sourceExe, string installDirectory)
	{
		if (string.IsNullOrWhiteSpace(sourceExe) || !File.Exists(sourceExe))
		{
			throw new FileNotFoundException("NorcomLauncher.exe was not next to the installer.");
		}
		Directory.CreateDirectory(installDirectory);
		string text = Path.Combine(installDirectory, "NorcomLauncher.exe");
		LauncherInstall.CopyLauncher(sourceExe, text);
		File.WriteAllText(Path.Combine(installDirectory, "installer-version.txt"), NorcomVersion.Current + Environment.NewLine);
		try
		{
			LauncherInstall.TrySyncAlt(text);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("alt sync " + ex.GetType().Name);
		}
		try
		{
			string home = Path.GetDirectoryName(Path.GetFullPath(installDirectory)) ?? installDirectory;
			LauncherCleanup.RemoveObsolete(text, home, null, Path.GetTempPath());
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup " + ex.GetType().Name);
		}
	}

	public static void PublishShortcuts(string primaryExe, bool createIfMissing)
	{
		if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(primaryExe) || !File.Exists(primaryExe))
		{
			return;
		}
		WindowsShortcut.PointAt(Path.Combine(NorcomPaths.DesktopDirectory, ShortcutName), primaryExe, createIfMissing, anyLauncherExe: true);
		if (NorcomPaths.DisposableProfile)
		{
			return;
		}
		string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
		if (!string.IsNullOrWhiteSpace(programs))
		{
			WindowsShortcut.PointAt(Path.Combine(programs, "NORCOM", ShortcutName), primaryExe, createIfMissing, anyLauncherExe: true);
		}
	}
}
