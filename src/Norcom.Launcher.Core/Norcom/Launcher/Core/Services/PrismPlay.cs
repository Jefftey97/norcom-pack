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
		try
		{
			PlayAccount.EnsureForPlay();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play account " + ex.GetType().Name);
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
		Task<int> run = process.RunAsync(prepared.Executable, prepared.Arguments, cancellationToken);
		Stopwatch open = Stopwatch.StartNew();
		while (!run.IsCompleted)
		{
			if (SeenGame(signals, process, out int? gamePid))
			{
				LauncherLog.Write("prism open minecraft started");
				NotePid(gamePid);
				NoteGame();
				Observe(run);
				return Started(prepared);
			}
			if (open.Elapsed >= openBudget)
			{
				LauncherLog.Write("prism open without minecraft");
				Observe(run);
				return new PrismPreparation
				{
					CanStart = false,
					Message = PrismMessages.NotDetected,
					Executable = prepared.Executable,
					Arguments = prepared.Arguments
				};
			}
			TimeSpan slice = openBudget - open.Elapsed;
			if (slice > TimeSpan.FromMilliseconds(500))
			{
				slice = TimeSpan.FromMilliseconds(500);
			}
			if (slice <= TimeSpan.Zero)
			{
				continue;
			}
			await Task.WhenAny(run, Task.Delay(slice, cancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
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
		if (!await GameStartedAsync(signals, probeBudget, cancellationToken, process).ConfigureAwait(continueOnCapturedContext: false))
		{
			LauncherLog.Write("prism closed without minecraft");
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
			if (gamePid != null)
			{
				return true;
			}
		}
		else if (signals.MinecraftProcessRunning())
		{
			return true;
		}
		return signals.LogFreshenedSinceLaunch();
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
