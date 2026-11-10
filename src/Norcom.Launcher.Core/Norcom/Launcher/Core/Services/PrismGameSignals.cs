using System;
using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public interface IPrismGameSignals
{
	bool MinecraftProcessRunning();

	bool LogFreshenedSinceLaunch();
}

public interface IGameProcessLookup
{
	int? TryGameProcessId(int? prismProcessId);
}

public readonly record struct GameProcessRow(string Name, string CommandLine, int Pid, int ParentPid);

public static class PrismGameSignals
{
	public static bool IsJavaProcess(string? processName)
	{
		string file = Path.GetFileName(processName ?? "");
		return file.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)
			|| file.Equals("java.exe", StringComparison.OrdinalIgnoreCase)
			|| file.Equals("javaw", StringComparison.OrdinalIgnoreCase)
			|| file.Equals("java", StringComparison.OrdinalIgnoreCase);
	}

	public static bool CommandLineIsGame(string? processName, string? commandLine, string? instanceHint)
	{
		if (!IsJavaProcess(processName) || string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		if (commandLine.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (commandLine.Contains("NORCOM", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(instanceHint) && commandLine.Contains(instanceHint, StringComparison.OrdinalIgnoreCase);
	}

	public static IEnumerable<string> LatestLogs(string? prismDataDir, string? gameDir)
	{
		if (!string.IsNullOrWhiteSpace(prismDataDir))
		{
			yield return Path.Combine(prismDataDir, "instances", PrismCommand.InstanceId, "minecraft", "logs", "latest.log");
		}
		if (!string.IsNullOrWhiteSpace(gameDir))
		{
			yield return Path.Combine(gameDir, "logs", "latest.log");
		}
	}

	public static int? SelectGamePid(IEnumerable<GameProcessRow> rows, string? instanceHint, int? prismPid)
	{
		if (rows == null)
		{
			return null;
		}
		int? any = null;
		int? hinted = null;
		foreach (GameProcessRow row in rows)
		{
			if (row.Pid <= 0 || !CommandLineIsGame(row.Name, row.CommandLine, instanceHint))
			{
				continue;
			}
			if (prismPid is int parent && parent > 0 && row.ParentPid == parent)
			{
				return row.Pid;
			}
			if (hinted == null && !string.IsNullOrWhiteSpace(instanceHint) && row.CommandLine.Contains(instanceHint, StringComparison.OrdinalIgnoreCase))
			{
				hinted = row.Pid;
			}
			if (any == null)
			{
				any = row.Pid;
			}
		}
		return hinted ?? any;
	}

	public static bool MtimeFreshened(DateTime? baselineUtc, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}
		if (baselineUtc == null)
		{
			return true;
		}
		return File.GetLastWriteTimeUtc(path) > baselineUtc.Value;
	}
}

public sealed class LivePrismGameSignals : IPrismGameSignals, IGameProcessLookup
{
	private readonly string _instanceDir;

	private readonly List<(string Path, DateTime? BaselineUtc)> _logs = new List<(string, DateTime?)>();

	public LivePrismGameSignals(string prismDataDir, string gameDir)
	{
		_instanceDir = Path.Combine(prismDataDir ?? "", "instances", PrismCommand.InstanceId);
		foreach (string path in PrismGameSignals.LatestLogs(prismDataDir, gameDir))
		{
			DateTime? baseline = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
			_logs.Add((path, baseline));
		}
	}

	public bool MinecraftProcessRunning()
	{
		return TryGameProcessId(null) != null;
	}

	public int? TryGameProcessId(int? prismProcessId)
	{
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return null;
			}
			List<Dictionary<string, object?>>? found = WmiQuery.Rows("SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name='javaw.exe' OR Name='java.exe'", "Name", "CommandLine", "ProcessId", "ParentProcessId");
			if (found == null)
			{
				return null;
			}
			List<GameProcessRow> rows = new List<GameProcessRow>();
			foreach (Dictionary<string, object?> item in found)
			{
				rows.Add(new GameProcessRow(WmiQuery.Text(item, "Name"), WmiQuery.Text(item, "CommandLine"), ToPid(WmiQuery.Number(item, "ProcessId")), ToPid(WmiQuery.Number(item, "ParentProcessId"))));
			}
			return PrismGameSignals.SelectGamePid(rows, _instanceDir, prismProcessId);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("prism game probe " + ex.GetType().Name);
			return null;
		}
	}

	public bool LogFreshenedSinceLaunch()
	{
		foreach ((string path, DateTime? baseline) in _logs)
		{
			if (PrismGameSignals.MtimeFreshened(baseline, path))
			{
				return true;
			}
		}
		return false;
	}

	private static int ToPid(long value)
	{
		return value > 0 && value <= int.MaxValue ? (int)value : 0;
	}
}
