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

public readonly record struct GameProcessRow(string Name, string CommandLine, int Pid, int ParentPid, string WorkingDirectory = "");

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

	public static bool CommandLineIsGame(string? processName, string? commandLine, string? instanceHint, string? gameDir = null)
	{
		if (!IsJavaProcess(processName) || string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		if (!HasClientMain(commandLine) || IsMinecraftServerCommand(commandLine))
		{
			return false;
		}
		return ContainsPath(commandLine, instanceHint) || ContainsPath(commandLine, gameDir);
	}

	public static bool HasClientMain(string? commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		return commandLine.Contains("net.fabricmc.loader.impl.launch.knot.KnotClient", StringComparison.Ordinal)
			|| commandLine.Contains("net.minecraft.client.main.Main", StringComparison.Ordinal);
	}

	public static bool IsMinecraftServerCommand(string? commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		return commandLine.Contains("fabric-server-launch", StringComparison.OrdinalIgnoreCase)
			|| commandLine.Contains("nogui", StringComparison.OrdinalIgnoreCase)
			|| commandLine.Contains("net.minecraft.server.Main", StringComparison.Ordinal)
			|| commandLine.Contains("server.jar", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsPrismLauncher(string? processName)
	{
		string file = Path.GetFileName(processName ?? "");
		return file.Equals("prismlauncher.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("prismlauncher", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsServerLocation(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string value = text.Replace('/', '\\');
		return value.Contains("\\NORCOM\\Server", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("\\MinecraftServer", StringComparison.OrdinalIgnoreCase)
			|| IsMinecraftServerCommand(text);
	}

	public static bool IsServerJava(string? processName, string? commandLine, string? workingDirectory = null)
	{
		return IsJavaProcess(processName) && (IsServerLocation(commandLine) || IsServerLocation(workingDirectory));
	}

	public static bool IsZomboidClient(string? processName, string? commandLine)
	{
		string file = Path.GetFileName(processName ?? "");
		string line = commandLine ?? "";
		if (IsZomboidServer(file, line))
		{
			return false;
		}
		if (file.Equals("ProjectZomboid64.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("ProjectZomboid64", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return IsJavaProcess(processName) && line.Contains("ProjectZomboid64", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsZomboidServer(string? processName, string? commandLine)
	{
		string file = Path.GetFileName(processName ?? "");
		string line = commandLine ?? "";
		if (file.Contains("ProjectZomboidServer", StringComparison.OrdinalIgnoreCase) || line.Contains("ProjectZomboidServer", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return line.Contains("zombie.network.GameServer", StringComparison.Ordinal) || line.Contains("StartServer", StringComparison.OrdinalIgnoreCase);
	}

	public static bool ShouldForgetLockedProcess(string? processName, string? commandLine)
	{
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		if (IsZomboidClient(processName, commandLine))
		{
			return false;
		}
		if (IsJavaProcess(processName) && HasClientMain(commandLine) && !IsMinecraftServerCommand(commandLine))
		{
			return false;
		}
		return IsJavaProcess(processName) || IsZomboidServer(processName, commandLine);
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

	public static int? SelectGamePid(IEnumerable<GameProcessRow> rows, string? instanceHint, int? prismPid, string? gameDir = null)
	{
		if (rows == null)
		{
			return null;
		}
		List<GameProcessRow> list = new List<GameProcessRow>(rows);
		Dictionary<int, int> parents = new Dictionary<int, int>();
		HashSet<int> prismPids = new HashSet<int>();
		if (prismPid is int passed && passed > 0)
		{
			prismPids.Add(passed);
		}
		foreach (GameProcessRow row in list)
		{
			if (row.Pid > 0)
			{
				parents[row.Pid] = row.ParentPid;
			}
			if (row.Pid > 0 && IsPrismLauncher(row.Name))
			{
				prismPids.Add(row.Pid);
			}
		}
		int? atInstance = null;
		int? startedChild = null;
		foreach (GameProcessRow row in list)
		{
			if (row.Pid <= 0 || !IsJavaProcess(row.Name) || IsServerJava(row.Name, row.CommandLine, row.WorkingDirectory))
			{
				continue;
			}
			bool here = PointsAtInstance(row.CommandLine, instanceHint, gameDir) || PointsAtInstance(row.WorkingDirectory, instanceHint, gameDir);
			bool direct = prismPid is int parent && parent > 0 && row.ParentPid == parent;
			if (direct && here)
			{
				return row.Pid;
			}
			if (here && OwnedByPrism(row.ParentPid, parents, prismPids) && atInstance == null)
			{
				atInstance = row.Pid;
			}
			if (direct && startedChild == null && (HasClientMain(row.CommandLine) || string.IsNullOrWhiteSpace(row.CommandLine)))
			{
				startedChild = row.Pid;
			}
		}
		return atInstance ?? startedChild;
	}

	private static bool OwnedByPrism(int parentPid, Dictionary<int, int> parents, HashSet<int> prismPids)
	{
		int pid = parentPid;
		HashSet<int> seen = new HashSet<int>();
		while (pid > 0 && seen.Add(pid))
		{
			if (prismPids.Contains(pid))
			{
				return true;
			}
			if (!parents.TryGetValue(pid, out int next))
			{
				return false;
			}
			pid = next;
		}
		return false;
	}

	private static bool PointsAtInstance(string? text, string? instanceHint, string? gameDir)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		return ContainsPath(text, instanceHint) || ContainsPath(text, gameDir);
	}

	private static bool ContainsPath(string commandLine, string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		if (commandLine.Contains(path, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string slash = path.Replace('\\', '/');
		if (!slash.Equals(path, StringComparison.Ordinal) && commandLine.Contains(slash, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string back = path.Replace('/', '\\');
		return !back.Equals(path, StringComparison.Ordinal) && commandLine.Contains(back, StringComparison.OrdinalIgnoreCase);
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

	private readonly string _gameDir;

	private readonly List<(string Path, DateTime? BaselineUtc)> _logs = new List<(string, DateTime?)>();

	public LivePrismGameSignals(string prismDataDir, string gameDir)
	{
		_instanceDir = Path.Combine(prismDataDir ?? "", "instances", PrismCommand.InstanceId);
		_gameDir = gameDir ?? "";
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
			List<Dictionary<string, object?>>? found = WmiQuery.Rows("SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process WHERE Name='javaw.exe' OR Name='java.exe' OR Name='prismlauncher.exe'", "Name", "CommandLine", "ProcessId", "ParentProcessId");
			if (found == null)
			{
				return null;
			}
			List<GameProcessRow> rows = new List<GameProcessRow>();
			foreach (Dictionary<string, object?> item in found)
			{
				string name = WmiQuery.Text(item, "Name");
				int pid = ToPid(WmiQuery.Number(item, "ProcessId"));
				string directory = PrismGameSignals.IsJavaProcess(name) ? ProcessWorkingDirectory.TryRead(pid) : "";
				rows.Add(new GameProcessRow(name, WmiQuery.Text(item, "CommandLine"), pid, ToPid(WmiQuery.Number(item, "ParentProcessId")), directory));
			}
			return PrismGameSignals.SelectGamePid(rows, _instanceDir, prismProcessId, _gameDir);
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
