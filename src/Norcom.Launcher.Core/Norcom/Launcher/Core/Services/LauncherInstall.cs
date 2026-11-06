using System;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public static class LauncherInstall
{
	public static string CurrentExe()
	{
		return Resolve(Environment.ProcessPath, FriendInstaller.InstalledExePath);
	}

	public static string Resolve(string? processPath, string canonicalInstall)
	{
		if (IsRunningLauncher(processPath) && !LauncherCleanup.IsObsoleteCopy(processPath))
		{
			return Path.GetFullPath(processPath!);
		}
		return Path.GetFullPath(canonicalInstall);
	}

	public static bool IsRunningLauncher(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		return Path.GetFileName(path).Equals(FriendInstaller.ExeName, StringComparison.OrdinalIgnoreCase);
	}
}
