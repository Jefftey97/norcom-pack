using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public sealed class ZomboidLaunchPlan
{
	public bool CanStart { get; init; }

	public string Url { get; init; } = ZomboidLaunch.SteamUrl;

	public string Message { get; init; } = "";

	public string GraphicsNote { get; init; } = "";

	public string GamePath { get; init; } = "";

	public bool IncludesPassword { get; init; }
}

public static class ZomboidLaunch
{
	public const string SteamUrl = "steam://rungameid/108600";

	public static ZomboidLaunchPlan Prepare(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath)
	{
		return Prepare(sources, userProfile, launcherHome, minecraftLockPath, null);
	}

	public static ZomboidLaunchPlan Prepare(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath, string feedJson)
	{
		SteamGameLocation install = SteamLibrary.FindZomboid(sources);
		if (!install.Found)
		{
			return new ZomboidLaunchPlan
			{
				CanStart = false,
				Message = SteamLibrary.NotFound
			};
		}
		ZomboidFeedDoc feed = ParseFeed(feedJson);
		string zomboidRoot = Path.Combine(userProfile, "Zomboid");
		try
		{
			Directory.CreateDirectory(zomboidRoot);
		}
		catch (Exception ex)
		{
			return new ZomboidLaunchPlan
			{
				CanStart = false,
				Message = "Project Zomboid settings could not be prepared (" + ex.GetType().Name + ")."
			};
		}
		string note = "";
		try
		{
			note = GameLaunchGraphics.ApplyBeforeLaunch("zomboid", install.GamePath, launcherHome, minecraftLockPath, userProfile);
		}
		catch (Exception ex)
		{
			note = "Zomboid graphics were not applied (" + ex.GetType().Name + ").";
		}
		string url = ZomboidFeed.SteamUrl(feed, out string passwordNote);
		string workshop = ZomboidFeed.WorkshopNotice(launcherHome, install.LibraryPath, feed.Workshop);
		string message = "Opening Project Zomboid in Steam. Workshop items are subscribed in Steam.";
		if (workshop.Length > 0)
		{
			message += " " + workshop;
		}
		if (passwordNote.Length > 0)
		{
			message += " " + passwordNote;
		}
		return new ZomboidLaunchPlan
		{
			CanStart = true,
			Url = url,
			Message = message,
			GraphicsNote = note,
			GamePath = install.GamePath,
			IncludesPassword = url.Contains("+password", StringComparison.Ordinal)
		};
	}

	public static async Task<ZomboidLaunchPlan> PrepareAsync(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath, string feedJson, HttpClient http, CancellationToken ct)
	{
		ZomboidLaunchPlan plan = Prepare(sources, userProfile, launcherHome, minecraftLockPath, feedJson);
		if (!plan.CanStart || http == null)
		{
			return plan;
		}
		ZomboidFeedDoc feed = ParseFeed(feedJson);
		if (feed.Files.Count == 0)
		{
			return plan;
		}
		try
		{
			await ZomboidFeed.SyncFilesAsync(http, Path.Combine(userProfile, "Zomboid"), feed, ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException or HttpRequestException)
		{
			return new ZomboidLaunchPlan
			{
				CanStart = false,
				Message = ex.Message,
				GraphicsNote = plan.GraphicsNote,
				GamePath = plan.GamePath
			};
		}
		return plan;
	}

	private static ZomboidFeedDoc ParseFeed(string feedJson)
	{
		try
		{
			return ZomboidFeed.Parse(feedJson);
		}
		catch (System.Text.Json.JsonException)
		{
			return new ZomboidFeedDoc();
		}
	}
}

public static class SteamStart
{
	public static Action<string> OpenUrl { get; set; } = DefaultOpen;

	public static void Open(string url)
	{
		OpenUrl(url);
	}

	private static void DefaultOpen(string url)
	{
		Process.Start(new ProcessStartInfo(url)
		{
			UseShellExecute = true
		});
	}
}

public static class ChosenGame
{
	public const string Minecraft = "minecraft";

	public const string Zomboid = "zomboid";

	public static string Normalize(string id)
	{
		if (string.Equals(id, Zomboid, StringComparison.OrdinalIgnoreCase))
		{
			return Zomboid;
		}
		return Minecraft;
	}

	public static string Load(string launcherHome)
	{
		try
		{
			string path = Path.Combine(launcherHome, "selected-game.txt");
			if (!File.Exists(path))
			{
				return Minecraft;
			}
			return Normalize(File.ReadAllText(path).Trim());
		}
		catch (IOException)
		{
			return Minecraft;
		}
	}

	public static void Save(string launcherHome, string id)
	{
		Directory.CreateDirectory(launcherHome);
		File.WriteAllText(Path.Combine(launcherHome, "selected-game.txt"), Normalize(id));
	}
}
