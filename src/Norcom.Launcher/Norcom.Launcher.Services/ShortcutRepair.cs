using System;
using System.IO;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Services;

public static class ShortcutRepair
{
	public static void EnsureKnownShortcuts(string exePath)
	{
		if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
		{
			return;
		}
		TryRetarget(Path.Combine(NorcomPaths.DesktopDirectory, "NORCOM Launcher.lnk"), exePath);
		if (!NorcomPaths.DisposableProfile)
		{
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				TryRetarget(Path.Combine(folderPath, "NORCOM", "NORCOM Launcher.lnk"), exePath);
			}
		}
	}

	private static void TryRetarget(string linkPath, string exePath)
	{
		try
		{
			if (!File.Exists(linkPath))
			{
				return;
			}
			Type typeFromProgID = Type.GetTypeFromProgID("WScript.Shell");
			if ((object)typeFromProgID != null)
			{
				dynamic val = Activator.CreateInstance(typeFromProgID);
				dynamic val2 = val.CreateShortcut(linkPath);
				if (LegacyRetirement.IsLegacyShortcutTarget(val2.TargetPath))
				{
					val2.TargetPath = exePath;
					val2.WorkingDirectory = Path.GetDirectoryName(exePath);
					val2.IconLocation = exePath + ",0";
					val2.Description = "NORCOM Launcher";
					val2.Save();
				}
			}
		}
		catch
		{
		}
	}
}
