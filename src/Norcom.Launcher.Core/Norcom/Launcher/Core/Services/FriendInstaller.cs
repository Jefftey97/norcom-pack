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
		string fullPath = Path.GetFullPath(sourceExe);
		string fullPath2 = Path.GetFullPath(text);
		if (!fullPath.Equals(fullPath2, StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(fullPath, text, overwrite: true);
		}
		File.WriteAllText(Path.Combine(installDirectory, "installer-version.txt"), NorcomVersion.Current + Environment.NewLine);
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
}
