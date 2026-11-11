using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class PrismPlay
{
	public static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(15);

	public static readonly TimeSpan OpenBudget = TimeSpan.FromSeconds(90);

	public static Action? GameObserved { get; set; }

	public static Action<int?>? GameProcessObserved { get; set; }

	public static Action<string>? StatusObserved { get; set; }

	public static Action<string> PlayFailed { get; set; } = ReportPlayFailure;

	public static PrismPreparation Prepare(string? prismEnvironment, IEnumerable<string> prismCandidates, string prismDataDir, string gameDir, string javaPath, int ramMb, bool applyMemory = false)
	{
		try
		{
			LauncherCleanup.BeforePlay();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup " + ex.GetType().Name);
		}
		try
		{
			GraphicsPlay.ApplyAuto(gameDir);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play graphics " + ex.GetType().Name);
		}
		string text = PrismInstall.Find(prismEnvironment, prismCandidates);
		if (text == null)
		{
			return new PrismPreparation
			{
				CanStart = false,
				Message = PrismMessages.Missing
			};
		}
		int heap = ramMb;
		try
		{
			PcHardware? hardware = GraphicsPlay.Probe?.Invoke() ?? HardwareProbe.TryRead();
			long hostRam = hardware != null && hardware.RamBytes > 0 ? hardware.RamBytes : HardwareProbe.TryHostRamBytes();
			heap = MemoryPlan.ForPlay(ramMb, hostRam, hardware?.GpuName);
			if (heap != ramMb)
			{
				LauncherLog.Write("play heap " + ramMb + " -> " + heap);
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play heap " + ex.GetType().Name);
		}
		bool applied = PrismInstance.Ensure(prismDataDir, gameDir, javaPath, heap, applyMemory);
		FriendsEndpoint endpoint = FriendsServer.Resolve();
		IReadOnlyList<string> arguments = PrismCommand.Build(endpoint.Host, endpoint.Port);
		return new PrismPreparation
		{
			CanStart = true,
			Applied = applied,
			Message = PrismMessages.Opening,
			Executable = text,
			Arguments = arguments
		};
	}

	public static Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken)
	{
		IPrismGameSignals signals = new LivePrismGameSignals(PrismCommand.DefaultDataDir(NorcomPaths.AppData), NorcomPaths.GameDir);
		return RunAsync(prepared, process, cancellationToken, signals, ProbeBudget, OpenBudget);
	}

	public static Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken, IPrismGameSignals signals, TimeSpan probeBudget)
	{
		return RunAsync(prepared, process, cancellationToken, signals, probeBudget, probeBudget);
	}

	public static async Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken, IPrismGameSignals signals, TimeSpan probeBudget, TimeSpan openBudget)
	{
		if (!prepared.CanStart || prepared.Executable == null)
		{
			return prepared;
		}
		IPrismWork work = signals as IPrismWork;
		// An open, idle Prism still gets --launch/--server: Prism hands them to the copy that
		// is already open. Only a Prism that is still downloading a previous launch is waited on,
		// so a second click cannot start the same instance twice.
		bool prismOpen = work != null && work.LauncherAlreadyRunning();
		bool hold = prismOpen && work!.DownloadMoving();
		Task<int> run = null;
		if (!hold)
		{
			if (prismOpen)
			{
				LauncherLog.Write("prism already running; forwarding launch");
			}
			run = process.RunAsync(prepared.Executable, prepared.Arguments, cancellationToken);
		}
		else
		{
			LauncherLog.Write("prism already running and busy");
		}
		Stopwatch idle = Stopwatch.StartNew();
		bool announced = false;
		while (run == null || !run.IsCompleted)
		{
			if (hold && work != null && !work.LauncherAlreadyRunning())
			{
				break;
			}
			if (SeenGame(signals, process, out int? gamePid))
			{
				LauncherLog.Write("prism open minecraft started");
				NotePid(gamePid);
				NoteGame();
				if (run != null)
				{
					Observe(run);
				}
				return Started(prepared);
			}
			if (work != null && work.DownloadMoving())
			{
				idle.Restart();
			}
			if (idle.Elapsed >= openBudget)
			{
				LauncherLog.Write("prism open without minecraft");
				NoteFailure(PrismMessages.NotDetected);
				if (run != null)
				{
					Observe(run);
				}
				return new PrismPreparation
				{
					CanStart = false,
					Message = PrismMessages.NotDetected,
					Executable = prepared.Executable,
					Arguments = prepared.Arguments
				};
			}
			if (!announced)
			{
				announced = true;
				NoteStatus(PrismMessages.Starting);
			}
			TimeSpan slice = openBudget - idle.Elapsed;
			if (slice > TimeSpan.FromMilliseconds(500))
			{
				slice = TimeSpan.FromMilliseconds(500);
			}
			if (slice <= TimeSpan.Zero)
			{
				continue;
			}
			if (run == null)
			{
				await Task.Delay(slice, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await Task.WhenAny(run, Task.Delay(slice, cancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		if (run == null)
		{
			if (!await GameStartedAsync(signals, openBudget > probeBudget ? openBudget : probeBudget, cancellationToken, process).ConfigureAwait(continueOnCapturedContext: false))
			{
				LauncherLog.Write("prism closed without minecraft");
				NoteFailure(PrismMessages.ClosedWithoutGame);
				return new PrismPreparation
				{
					CanStart = false,
					Message = PrismMessages.ClosedWithoutGame,
					Executable = prepared.Executable,
					Arguments = prepared.Arguments
				};
			}
			LauncherLog.Write("prism closed minecraft started");
			return Started(prepared);
		}
		if (await run.ConfigureAwait(continueOnCapturedContext: false) != 0)
		{
			return new PrismPreparation
			{
				CanStart = false,
				Message = PrismMessages.Rejected,
				Executable = prepared.Executable,
				Arguments = prepared.Arguments
			};
		}
		TimeSpan handoff = openBudget > probeBudget ? openBudget : probeBudget;
		if (!await GameStartedAsync(signals, handoff, cancellationToken, process).ConfigureAwait(continueOnCapturedContext: false))
		{
			LauncherLog.Write("prism closed without minecraft");
			NoteFailure(PrismMessages.ClosedWithoutGame);
			return new PrismPreparation
			{
				CanStart = false,
				Message = PrismMessages.ClosedWithoutGame,
				Executable = prepared.Executable,
				Arguments = prepared.Arguments
			};
		}
		LauncherLog.Write("prism closed minecraft started");
		return Started(prepared);
	}

	private static void NoteGame()
	{
		try
		{
			GameObserved?.Invoke();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game observed " + ex.GetType().Name);
		}
	}

	private static void NoteStatus(string text)
	{
		try
		{
			StatusObserved?.Invoke(text);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play status " + ex.GetType().Name);
		}
	}

	private static void NoteFailure(string message)
	{
		try
		{
			PlayFailed?.Invoke(message);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play report " + ex.GetType().Name);
		}
	}

	private static void ReportPlayFailure(string message)
	{
		try
		{
			CrashReportService.Queue("play", message, null);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play report " + ex.GetType().Name);
		}
	}

	private static void NotePid(int? pid)
	{
		try
		{
			GameProcessObserved?.Invoke(pid);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game pid " + ex.GetType().Name);
		}
	}

	private static bool SeenGame(IPrismGameSignals signals, IPrismProcess process, out int? gamePid)
	{
		gamePid = null;
		if (signals is IGameProcessLookup lookup)
		{
			gamePid = lookup.TryGameProcessId(TrackedPid(process));
			return gamePid != null;
		}
		return signals.MinecraftProcessRunning();
	}

	private static int? TrackedPid(IPrismProcess process)
	{
		return (process as ITrackedPrismProcess)?.ProcessId;
	}

	private static PrismPreparation Started(PrismPreparation prepared)
	{
		return new PrismPreparation
		{
			CanStart = true,
			Message = PrismMessages.Closed,
			Executable = prepared.Executable,
			Arguments = prepared.Arguments
		};
	}

	private static void Observe(Task task)
	{
		_ = task.ContinueWith(delegate(Task done)
		{
			if (done.IsFaulted)
			{
				LauncherLog.Write("prism exit " + done.Exception?.InnerException?.GetType().Name);
			}
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	public static async Task<bool> GameStartedAsync(IPrismGameSignals signals, TimeSpan budget, CancellationToken cancellationToken, IPrismProcess process = null)
	{
		Stopwatch watch = Stopwatch.StartNew();
		bool announced = false;
		while (true)
		{
			if (SeenGame(signals, process, out int? gamePid))
			{
				NotePid(gamePid);
				NoteGame();
				return true;
			}
			if (watch.Elapsed >= budget)
			{
				return false;
			}
			if (!announced)
			{
				announced = true;
				NoteStatus(PrismMessages.Starting);
			}
			TimeSpan remaining = budget - watch.Elapsed;
			TimeSpan slice = TimeSpan.FromMilliseconds(500);
			if (remaining < slice)
			{
				slice = remaining;
			}
			if (slice <= TimeSpan.Zero)
			{
				return false;
			}
			await Task.Delay(slice, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
	}
}
