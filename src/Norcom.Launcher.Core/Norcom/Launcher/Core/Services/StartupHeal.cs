using System;
using System.Collections.Generic;
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
		long? ticks=null;
        try{using var process=Process.GetProcessById(pid);ticks=process.StartTime.ToUniversalTime().Ticks;}catch{ }
        File.WriteAllText(lockPath,"pid="+pid+"\nkind=game\nstarted="+DateTimeOffset.UtcNow.ToString("o")+"\n"+(ticks.HasValue?"processStartedUtcTicks="+ticks.Value+"\n":""));
	}

    public static long? ReadGameStartTicks(string path)
    {
        try
        {
            string value=null;
            foreach(var row in File.ReadAllLines(path))
                if(row.StartsWith("processStartedUtcTicks=",StringComparison.Ordinal))
                {if(value!=null)return null;value=row.Substring("processStartedUtcTicks=".Length);}
            return long.TryParse(value,out var ticks) && ticks>0?ticks:null;
        }
        catch{return null;}
    }

	public static Func<int, (string Name, string CommandLine)?>? CommandReader { get; set; }

	public static bool TryReadProcessCommand(int pid, out string name, out string commandLine)
	{
		name = "";
		commandLine = "";
		if (pid <= 0)
		{
			return false;
		}
		if (CommandReader != null)
		{
			(string Name, string CommandLine)? row = CommandReader(pid);
			if (row == null)
			{
				return false;
			}
			name = row.Value.Name ?? "";
			commandLine = row.Value.CommandLine ?? "";
			return name.Length > 0 || commandLine.Length > 0;
		}
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}
		try
		{
			List<Dictionary<string, object?>>? rows = WmiQuery.Rows("SELECT Name, CommandLine FROM Win32_Process WHERE ProcessId=" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "Name", "CommandLine");
			if (rows == null || rows.Count == 0)
			{
				return false;
			}
			name = WmiQuery.Text(rows[0], "Name");
			commandLine = WmiQuery.Text(rows[0], "CommandLine");
			return name.Length > 0 || commandLine.Length > 0;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("process command " + ex.GetType().Name);
			return false;
		}
	}

	public static bool ReleaseForeignClientLock(string lockPath)
	{
		int? pid = ReadGamePid(lockPath);
		long? ticks = ReadGameStartTicks(lockPath);
		if (pid is not int id || id <= 0 || ticks is not > 0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById(id);
			if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != ticks.Value)
			{
				return false;
			}
			if (!TryReadProcessCommand(id, out string name, out string commandLine))
			{
				return false;
			}
			if (!PrismGameSignals.ShouldForgetLockedProcess(name, commandLine))
			{
				return false;
			}
			LaunchedGame.Forget(lockPath);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game lock " + ex.GetType().Name);
			return false;
		}
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
						var ticks=ReadGameStartTicks(lockPath);
                            if(ticks.HasValue){using var process=Process.GetProcessById(result);return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==ticks.Value;}
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
