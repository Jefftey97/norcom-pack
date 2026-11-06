using System;
using System.IO;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Services;

public static class ShortcutRepair
{
	public static void EnsureKnownShortcuts(string exePath)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		string primary = FriendInstaller.InstalledExePath;
		if (File.Exists(primary))
		{
			FriendInstaller.PublishShortcuts(primary, createIfMissing: false);
			return;
		}
		if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
		{
			return;
		}
		WindowsShortcut.PointAt(Path.Combine(NorcomPaths.DesktopDirectory, FriendInstaller.ShortcutName), exePath, createIfMissing: false, anyLauncherExe: false);
		if (!NorcomPaths.DisposableProfile)
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				WindowsShortcut.PointAt(Path.Combine(folderPath, "NORCOM", FriendInstaller.ShortcutName), exePath, createIfMissing: false, anyLauncherExe: false);
			}
		}
	}
}
