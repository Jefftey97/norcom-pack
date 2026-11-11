using System;
using System.Diagnostics;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public readonly struct PlayAdmission
{
	public bool Launch { get; }

	public string Message { get; }

	private PlayAdmission(bool launch, string message)
	{
		Launch = launch;
		Message = message ?? "";
	}

	public static PlayAdmission Go()
	{
		return new PlayAdmission(true, "");
	}

	public static PlayAdmission Stop(string message)
	{
		return new PlayAdmission(false, message);
	}

	public const string AlreadyRunning = "Minecraft is already running. Close it from its own window, or press Stop, then press Play.";

	public const string UnreadableProcess = "NORCOM could not read the recorded Minecraft process, so a second game was not started. Close Minecraft if it is open, or press Stop, then press Play.";

	public const string UnreadableLock = "NORCOM could not read the game status file. Close Minecraft if it is open, then press Play again.";

	public static PlayAdmission ForMinecraft(string lockPath)
	{
		if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
		{
			return Go();
		}
		try
		{
			if (new FileInfo(lockPath).Length > 8192)
			{
				LaunchedGame.Forget(lockPath);
				return Go();
			}
		}
		catch (IOException)
		{
			return Stop(UnreadableLock);
		}
		catch (UnauthorizedAccessException)
		{
			return Stop(UnreadableLock);
		}
		int? pid = StartupHeal.ReadGamePid(lockPath);
		if (pid is not int id || id <= 0)
		{
			LaunchedGame.Forget(lockPath);
			return Go();
		}
		bool alive;
		long actualTicks;
		try
		{
			using Process process = Process.GetProcessById(id);
			alive = !process.HasExited;
			actualTicks = alive ? process.StartTime.ToUniversalTime().Ticks : 0;
		}
		catch (ArgumentException)
		{
			LaunchedGame.Forget(lockPath);
			return Go();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play lock " + ex.GetType().Name);
			return Stop(UnreadableProcess);
		}
		long? recorded = StartupHeal.ReadGameStartTicks(lockPath);
		if (!alive || (recorded is > 0 && actualTicks != recorded.Value))
		{
			LaunchedGame.Forget(lockPath);
			return Go();
		}
		if (!StartupHeal.TryReadProcessCommand(id, out string name, out string commandLine))
		{
			return Stop(UnreadableProcess);
		}
		if (PrismGameSignals.ShouldForgetLockedProcess(name, commandLine))
		{
			LaunchedGame.Forget(lockPath);
			return Go();
		}
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return Stop(UnreadableProcess);
		}
		return Stop(AlreadyRunning);
	}
}
