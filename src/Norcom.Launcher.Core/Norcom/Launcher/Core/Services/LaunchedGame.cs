using System;
using System.Diagnostics;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class LaunchedGame
{
	public static string PlayCaption(bool gameRunning)
	{
		return gameRunning ? "Playing" : "Play";
	}

	public static void Remember(string lockPath, int pid)
	{
		if (pid <= 0)
		{
			return;
		}
		StartupHeal.WriteGameLock(lockPath, pid);
	}

	public static void Forget(string lockPath)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(lockPath) && File.Exists(lockPath))
			{
				File.Delete(lockPath);
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game lock " + ex.GetType().Name);
		}
	}

	public static bool TryStop(int pid,long? expectedStartedUtcTicks=null)
	{
		if (pid <= 0 || pid == Environment.ProcessId || expectedStartedUtcTicks is not >0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById(pid);
            if(process.StartTime.ToUniversalTime().Ticks!=expectedStartedUtcTicks)return false;
			if (process.HasExited)
			{
				return true;
			}
			try
			{
				if (process.CloseMainWindow() && process.WaitForExit(4000))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				LauncherLog.Write("game close " + ex.GetType().Name);
			}
			if (process.HasExited)
			{
				return true;
			}
			process.Kill(entireProcessTree: true);
			return process.WaitForExit(4000) || process.HasExited;
		}
		catch (ArgumentException)
		{
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game stop " + ex.GetType().Name);
			return false;
		}
	}
}
