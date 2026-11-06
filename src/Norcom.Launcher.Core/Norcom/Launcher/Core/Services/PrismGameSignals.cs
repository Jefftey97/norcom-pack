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

public sealed class LivePrismGameSignals : IPrismGameSignals
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
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			Type? searcherType = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
			if (searcherType == null)
			{
				return false;
			}
			object searcher = Activator.CreateInstance(searcherType, "SELECT Name, CommandLine FROM Win32_Process WHERE Name='javaw.exe' OR Name='java.exe'")!;
			object collection = searcherType.GetMethod("Get")!.Invoke(searcher, null)!;
			foreach (object item in (System.Collections.IEnumerable)collection)
			{
				string name = ReadField(item, "Name");
				string command = ReadField(item, "CommandLine");
				if (PrismGameSignals.CommandLineIsGame(name, command, _instanceDir))
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("prism game probe " + ex.GetType().Name);
		}
		return false;
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

	private static string ReadField(object item, string name)
	{
		object? value = item.GetType().GetProperty("Item", new Type[] { typeof(string) })?.GetValue(item, new object[] { name });
		return value?.ToString() ?? "";
	}
}
