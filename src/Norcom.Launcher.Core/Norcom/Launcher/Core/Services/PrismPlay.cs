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

	public static Action? GameObserved { get; set; }

	public static PrismPreparation Prepare(string? prismEnvironment, IEnumerable<string> prismCandidates, string prismDataDir, string gameDir, string javaPath, int ramMb)
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
		PrismInstance.Ensure(prismDataDir, gameDir, javaPath, heap);
		FriendsEndpoint endpoint = FriendsServer.Resolve();
		IReadOnlyList<string> arguments = PrismCommand.Build(endpoint.Host, endpoint.Port);
		return new PrismPreparation
		{
			CanStart = true,
			Message = PrismMessages.Opening,
			Executable = text,
			Arguments = arguments
		};
	}

	public static Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken)
	{
		IPrismGameSignals signals = new LivePrismGameSignals(PrismCommand.DefaultDataDir(NorcomPaths.AppData), NorcomPaths.GameDir);
		return RunAsync(prepared, process, cancellationToken, signals, ProbeBudget);
	}

	public static async Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken, IPrismGameSignals signals, TimeSpan probeBudget)
	{
		if (!prepared.CanStart || prepared.Executable == null)
		{
			return prepared;
		}
		Task<int> run = process.RunAsync(prepared.Executable, prepared.Arguments, cancellationToken);
		while (!run.IsCompleted)
		{
			if (signals.MinecraftProcessRunning() || signals.LogFreshenedSinceLaunch())
			{
				LauncherLog.Write("prism open minecraft started");
				NoteGame();
				Observe(run);
				return Started(prepared);
			}
			await Task.WhenAny(run, Task.Delay(500, cancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
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
		if (!await GameStartedAsync(signals, probeBudget, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
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

	public static async Task<bool> GameStartedAsync(IPrismGameSignals signals, TimeSpan budget, CancellationToken cancellationToken)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (true)
		{
			if (signals.MinecraftProcessRunning() || signals.LogFreshenedSinceLaunch())
			{
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
