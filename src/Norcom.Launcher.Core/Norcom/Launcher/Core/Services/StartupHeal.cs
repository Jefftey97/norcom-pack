using System;
using System.Diagnostics;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class StartupHeal
{
	public static SingleInstanceDecision Decide(bool ownsMutex, bool activateAcknowledged, bool killedStuck)
	{
		if (ownsMutex && !killedStuck)
		{
			return SingleInstanceDecision.Continue;
		}
		if (ownsMutex && killedStuck)
		{
			return SingleInstanceDecision.ReclaimedStuckLauncher;
		}
		if (activateAcknowledged)
		{
			return SingleInstanceDecision.ExitBecauseOtherIsAlive;
		}
		return SingleInstanceDecision.ReclaimedStuckLauncher;
	}

	public static void ClearStale(string launcherDir, string gameDir)
	{
		Directory.CreateDirectory(launcherDir);
		try
		{
			string live = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(live) || !Path.GetFileName(live).Equals("NorcomLauncher.exe", StringComparison.OrdinalIgnoreCase))
			{
				live = Path.Combine(launcherDir, "NorcomLauncher.exe");
			}
			string home = Path.GetDirectoryName(Path.GetFullPath(launcherDir)) ?? launcherDir;
			LauncherCleanup.RemoveObsolete(live, home, gameDir, Path.GetTempPath());
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup " + ex.GetType().Name);
		}
		ClearLockIfDead(Path.Combine(launcherDir, "game.lock"));
		try
		{
			foreach (string item in Directory.EnumerateFiles(gameDir, "*.part", SearchOption.AllDirectories))
			{
				try
				{
					if (File.GetLastWriteTimeUtc(item) < DateTime.UtcNow.AddHours(-6.0))
					{
						File.Delete(item);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	public static void ClearLockIfDead(string lockPath)
	{
		if (!File.Exists(lockPath))
		{
			return;
		}
		try
		{
			string text = File.ReadAllText(lockPath);
			int num = 0;
			string[] array = text.Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i].Trim();
				if (text2.StartsWith("pid=", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text2;
					if (int.TryParse(text3.Substring(4, text3.Length - 4).Trim(), out var result))
					{
						num = result;
					}
				}
			}
			if (num <= 0 || !IsAlive(num))
			{
				File.Delete(lockPath);
			}
		}
		catch
		{
			try
			{
				File.Delete(lockPath);
			}
			catch
			{
			}
		}
	}

	public static bool IsAlive(int pid)
	{
		try
		{
			return !Process.GetProcessById(pid).HasExited;
		}
		catch
		{
			return false;
		}
	}

	public static void WriteGameLock(string lockPath, int pid)
	{
		string directoryName = Path.GetDirectoryName(lockPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		File.WriteAllText(lockPath, "pid=" + pid + "\nkind=game\nstarted=" + DateTimeOffset.UtcNow.ToString("o") + "\n");
	}

	public static bool GameLockAlive(string lockPath)
	{
		if (!File.Exists(lockPath))
		{
			return false;
		}
		try
		{
			string[] array = File.ReadAllText(lockPath).Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.StartsWith("pid=", StringComparison.OrdinalIgnoreCase))
				{
					string text2 = text;
					if (int.TryParse(text2.Substring(4, text2.Length - 4).Trim(), out var result))
					{
						return IsAlive(result);
					}
				}
			}
		}
		catch
		{
			return false;
		}
		return false;
	}
}
