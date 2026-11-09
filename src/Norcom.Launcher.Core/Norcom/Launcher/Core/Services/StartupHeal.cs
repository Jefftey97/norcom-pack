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
			UpdateReport.ClearRejected(launcherDir);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("update clear " + ex.GetType().Name);
		}
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
		// Repair shares the updater's conservative interpretation. Unknown/locked/
		// malformed state is retained, never treated as proof that a game stopped.
		_ = Norcom.Launcher.Core.Updates.GameUpdateGuard.IsBlocked(lockPath);
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

	public static int? ReadGamePid(string lockPath)
	{
		if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
		{
			return null;
		}
		try
		{
			string[] array = File.ReadAllText(lockPath).Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				string text = array[i].Trim();
				if (text.StartsWith("pid=", StringComparison.OrdinalIgnoreCase) && int.TryParse(text.Substring(4).Trim(), out var result) && result > 0)
				{
					return result;
				}
			}
		}
		catch
		{
			return null;
		}
		return null;
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
