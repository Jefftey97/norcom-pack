using System;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class ShortcutTargets
{
	public static bool ShouldRetarget(string? currentTarget, string desiredExe, bool anyLauncherExe)
	{
		if (string.IsNullOrWhiteSpace(currentTarget) || string.IsNullOrWhiteSpace(desiredExe))
		{
			return false;
		}
		try
		{
			if (string.Equals(Path.GetFullPath(currentTarget), Path.GetFullPath(desiredExe), StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		catch
		{
			if (string.Equals(currentTarget.Trim(), desiredExe.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		if (LegacyRetirement.IsLegacyShortcutTarget(currentTarget))
		{
			return true;
		}
		if (!anyLauncherExe)
		{
			return false;
		}
		return Path.GetFileName(currentTarget.Trim().Trim('"')).Equals(FriendInstaller.ExeName, StringComparison.OrdinalIgnoreCase);
	}
}

public static class WindowsShortcut
{
	public static bool PointAt(string linkPath, string exePath, bool createIfMissing, bool anyLauncherExe)
	{
		if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(linkPath) || string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
		{
			return false;
		}
		try
		{
			bool exists = File.Exists(linkPath);
			if (!exists && !createIfMissing)
			{
				return false;
			}
			Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
			if (shellType == null)
			{
				return false;
			}
			dynamic shell = Activator.CreateInstance(shellType);
			if (!exists)
			{
				string? parent = Path.GetDirectoryName(linkPath);
				if (!string.IsNullOrEmpty(parent))
				{
					Directory.CreateDirectory(parent);
				}
			}
			dynamic shortcut = shell.CreateShortcut(linkPath);
			if (exists)
			{
				string? current = null;
				try
				{
					current = shortcut.TargetPath;
				}
				catch
				{
					current = null;
				}
				if (!ShortcutTargets.ShouldRetarget(current, exePath, anyLauncherExe))
				{
					return false;
				}
			}
			shortcut.TargetPath = exePath;
			shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
			shortcut.IconLocation = exePath + ",0";
			shortcut.Description = "NORCOM Launcher";
			shortcut.Save();
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("shortcut " + ex.GetType().Name);
			return false;
		}
	}
}
