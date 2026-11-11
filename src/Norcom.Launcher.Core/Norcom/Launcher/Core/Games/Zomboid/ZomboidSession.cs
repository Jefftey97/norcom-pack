using System;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Zomboid;

public static class ZomboidSession
{
	public static bool IsClient(string processName, string commandLine)
	{
		return PrismGameSignals.IsZomboidClient(processName, commandLine);
	}

	public static bool Running(string lockPath)
	{
		_ = lockPath;
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			System.Diagnostics.Process[] found = System.Diagnostics.Process.GetProcessesByName("ProjectZomboid64");
			try
			{
				for (int i = 0; i < found.Length; i++)
				{
					StartupHeal.TryReadProcessCommand(found[i].Id, out string name, out string commandLine);
					if (string.IsNullOrEmpty(name))
					{
						name = found[i].ProcessName;
					}
					if (IsClient(name, commandLine))
					{
						return true;
					}
				}
			}
			finally
			{
				for (int i = 0; i < found.Length; i++)
				{
					found[i].Dispose();
				}
			}
			return false;
		}
		catch
		{
			return true;
		}
	}
}
