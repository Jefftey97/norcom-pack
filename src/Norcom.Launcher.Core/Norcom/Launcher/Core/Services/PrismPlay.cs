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
		PrismInstance.Ensure(prismDataDir, gameDir, javaPath, ramMb);
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
		if (await process.RunAsync(prepared.Executable, prepared.Arguments, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) != 0)
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
		return new PrismPreparation
		{
			CanStart = true,
			Message = PrismMessages.Closed,
			Executable = prepared.Executable,
			Arguments = prepared.Arguments
		};
	}

	public static async Task<bool> GameStartedAsync(IPrismGameSignals signals, TimeSpan budget, CancellationToken cancellationToken)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (true)
		{
			if (signals.MinecraftProcessRunning() || signals.LogFreshenedSinceLaunch())
			{
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
