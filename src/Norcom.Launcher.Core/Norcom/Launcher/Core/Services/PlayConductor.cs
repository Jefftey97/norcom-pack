using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class PlayStoppedException : Exception
{
	public string Label { get; }

	public PlayStoppedException(string label, string message)
		: base(message)
	{
		Label = label ?? "";
	}
}

public sealed class PlayOutcome
{
	public bool Started { get; init; }

	public string Label { get; init; } = "";

	public string Message { get; init; } = "";

	public static PlayOutcome Launched(string label, string message)
	{
		return new PlayOutcome { Started = true, Label = label, Message = message ?? "" };
	}

	public static PlayOutcome Blocked(string label, string message)
	{
		return new PlayOutcome { Started = false, Label = label, Message = message ?? "" };
	}
}

public sealed class MinecraftPlayRequest
{
	public bool Busy { get; init; }

	public string LockPath { get; init; } = "";

	public bool ExistingInstall { get; init; }

	public Func<CancellationToken, Task> PollPin { get; init; }

	public Func<CancellationToken, Task<string>> EnsureJava { get; init; }

	public Func<CancellationToken, Task> SyncPack { get; init; }

	public Action AfterSync { get; init; }

	/// <summary>Installs Prism when it is missing. A failure here is logged and PreparePrism reports it.</summary>
	public Func<CancellationToken, Task> EnsurePrism { get; init; }

	public Func<string, PrismPreparation> PreparePrism { get; init; }

	public Func<PrismPreparation, CancellationToken, Task<PrismPreparation>> RunPrism { get; init; }

	public Func<bool> ClientRunning { get; init; }
}

public sealed class ZomboidPlayRequest
{
	public bool Busy { get; init; }

	public bool AlreadyRunning { get; init; }

	public ZomboidLaunchPlan Plan { get; init; }

	public Action<string> Open { get; init; }

	public string FeedNote { get; init; } = "";
}

public static class PlayInstall
{
	public static bool HasLocalClient(string gameDir)
	{
		if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
		{
			return false;
		}
		if (File.Exists(Path.Combine(gameDir, "options.txt")))
		{
			return true;
		}
		string mods = Path.Combine(gameDir, "mods");
		if (!Directory.Exists(mods))
		{
			return false;
		}
		foreach (string _ in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
		{
			return true;
		}
		return false;
	}
}

public static class PlayFeeds
{
	public static readonly TimeSpan FreshFor = TimeSpan.FromDays(14);

	public const string FeedDown = "The Project Zomboid pack feed could not be reached. Starting the installed game without a pack sync.";

	public const string UsingCache = "The Project Zomboid pack feed could not be reached. Using the saved pack list.";

	public const string ExpiredCache = "The saved Project Zomboid pack list is more than 14 days old and the feed could not be reached. Starting the game with that saved list.";

	public static string Choose(string live, string cached, DateTimeOffset? cachedUtc, DateTimeOffset now, out string note)
	{
		if (!string.IsNullOrWhiteSpace(live))
		{
			note = "";
			return live;
		}
		if (string.IsNullOrWhiteSpace(cached))
		{
			note = FeedDown;
			return "";
		}
		if (cachedUtc != null && now - cachedUtc.Value > FreshFor)
		{
			note = ExpiredCache;
			return cached;
		}
		note = UsingCache;
		return cached;
	}
}

public static class PlayConductor
{
	public const string AlreadyStarting = "Play is already starting. Wait for it to finish, then press Play again if the game did not open.";

	public const string JavaFailed = "Java 25 is missing or could not be prepared. Check the connection if a download is required, then press Play again.";

	public const string PackFailed = "The pack could not be downloaded, so a new install was not started. Check your connection, then press Play again.";

	public const string PackSkipped = "The pack feed could not be reached. Starting the installed client. Press Play again later to update.";

	public const string ZomboidRunning = "Project Zomboid is already running. Close it before pressing Play again.";

	public const string NoSteamLink = "Project Zomboid did not get a Steam launch link. Press Play again.";

	public static async Task<PlayOutcome> MinecraftAsync(MinecraftPlayRequest request, CancellationToken ct)
	{
		if (request.Busy)
		{
			return PlayOutcome.Blocked("Play is already starting", AlreadyStarting);
		}
		PlayAdmission admission = PlayAdmission.ForMinecraft(request.LockPath);
		if (!admission.Launch)
		{
			return PlayOutcome.Blocked("Minecraft was not started", admission.Message);
		}
		if (request.PollPin != null)
		{
			try
			{
				await request.PollPin(ct).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				LauncherLog.Write("play pin " + ex.GetType().Name);
			}
		}
		string java;
		try
		{
			java = request.EnsureJava == null ? "" : await request.EnsureJava(ct).ConfigureAwait(false);
		}
		catch (PlayStoppedException stop)
		{
			return PlayOutcome.Blocked(stop.Label, stop.Message);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play java " + ex.GetType().Name);
			return PlayOutcome.Blocked("Java is required", JavaFailed + " " + Redactor.Clean(ex.Message));
		}
		if (string.IsNullOrWhiteSpace(java))
		{
			return PlayOutcome.Blocked("Java is required", JavaFailed);
		}
		string feedNote = "";
		try
		{
			if (request.SyncPack != null)
			{
				await request.SyncPack(ct).ConfigureAwait(false);
			}
		}
		catch (PlayStoppedException stop)
		{
			return PlayOutcome.Blocked(stop.Label, stop.Message);
		}
		catch (PinMismatchException ex)
		{
			return PlayOutcome.Blocked("Pack check failed", string.IsNullOrWhiteSpace(ex.Message) ? PinGate.BlockCopy : ex.Message);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play pack " + ex.GetType().Name);
			if (!request.ExistingInstall)
			{
				return PlayOutcome.Blocked("Pack download failed", PackFailed);
			}
			feedNote = PackSkipped;
		}
		try
		{
			request.AfterSync?.Invoke();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play settings " + ex.GetType().Name);
		}
		string prismNote = "";
		if (request.EnsurePrism != null)
		{
			try
			{
				await request.EnsurePrism(ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				LauncherLog.Write("play prism install " + ex.GetType().Name);
				prismNote = " " + Redactor.Clean(ex.Message);
			}
		}
		PrismPreparation prepared;
		try
		{
			prepared = request.PreparePrism == null ? new PrismPreparation { CanStart = false, Message = PrismMessages.Missing } : request.PreparePrism(java);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play prism " + ex.GetType().Name);
			return PlayOutcome.Blocked("Prism is required", PrismMessages.Missing + " " + Redactor.Clean(ex.Message));
		}
		if (prepared == null || !prepared.CanStart || prepared.Executable == null)
		{
			string message = prepared == null || string.IsNullOrWhiteSpace(prepared.Message) ? PrismMessages.Missing : prepared.Message;
			return PlayOutcome.Blocked("Prism is required", message + prismNote);
		}
		PrismPreparation ran;
		try
		{
			ran = request.RunPrism == null ? prepared : await request.RunPrism(prepared, ct).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play prism run " + ex.GetType().Name);
			return PlayOutcome.Blocked("Prism did not start Minecraft", PrismMessages.Rejected + " " + Redactor.Clean(ex.Message));
		}
		bool running = false;
		try
		{
			running = request.ClientRunning != null && request.ClientRunning();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play running " + ex.GetType().Name);
		}
		string detail = Join(feedNote, ran == null ? "" : ran.Message);
		if (running || (ran != null && ran.CanStart))
		{
			return PlayOutcome.Launched(running ? "Playing" : "Prism closed", detail);
		}
		return PlayOutcome.Blocked("Prism did not start Minecraft", string.IsNullOrWhiteSpace(detail) ? PrismMessages.Rejected : detail);
	}

	public static PlayOutcome Zomboid(ZomboidPlayRequest request)
	{
		if (request.Busy)
		{
			return PlayOutcome.Blocked("Play is already starting", AlreadyStarting);
		}
		if (request.AlreadyRunning)
		{
			return PlayOutcome.Blocked("Project Zomboid is already running", ZomboidRunning);
		}
		ZomboidLaunchPlan plan = request.Plan;
		if (plan == null || !plan.CanStart)
		{
			string message = plan == null || string.IsNullOrWhiteSpace(plan.Message) ? SteamLibrary.NotFound : plan.Message;
			return PlayOutcome.Blocked(message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? SteamLibrary.NotFound : "Project Zomboid was not started", message);
		}
		if (string.IsNullOrWhiteSpace(plan.Url) || request.Open == null)
		{
			return PlayOutcome.Blocked("Project Zomboid was not started", NoSteamLink);
		}
		request.Open(plan.Url);
		return PlayOutcome.Launched("Opening Steam", Join(request.FeedNote, plan.Message));
	}

	private static string Join(string note, string message)
	{
		if (string.IsNullOrWhiteSpace(note))
		{
			return message ?? "";
		}
		if (string.IsNullOrWhiteSpace(message))
		{
			return note;
		}
		return note + " " + message;
	}
}
