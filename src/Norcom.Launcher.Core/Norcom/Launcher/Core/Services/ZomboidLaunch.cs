using System;
using System.Collections.Generic;
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

	public bool Applied { get; init; }
}

public static class ZomboidLaunch
{
	public const string SteamUrl = "steam://rungameid/108600";

	public static ZomboidLaunchPlan Prepare(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath)
	{
		return Prepare(sources, userProfile, launcherHome, minecraftLockPath, null);
	}

	public static ZomboidLaunchPlan Prepare(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath, string feedJson, string localPassword = "", int ramMb = 0, bool saveMemory = true)
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
		string memoryNote = "";
		bool applied = false;
		if (ramMb >= 512 && !saveMemory)
		{
			memoryNote = "Memory for Project Zomboid is " + ramMb.ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB.";
		}
		if (ramMb >= 512 && saveMemory)
		{
			try
			{
				ZomboidMemory.Apply(zomboidRoot, install.GamePath, ramMb);
				applied = true;
				memoryNote = "Memory for Project Zomboid is " + ramMb.ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB, saved in your Zomboid folder.";
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
			{
				memoryNote = "Project Zomboid memory was not saved (" + ex.GetType().Name + ").";
			}
		}
		string url = ZomboidMemory.WithHeap(ZomboidFeed.SteamUrl(feed, localPassword, out string passwordNote), ramMb);
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
		if (memoryNote.Length > 0)
		{
			message += " " + memoryNote;
		}
		return new ZomboidLaunchPlan
		{
			CanStart = true,
			Url = url,
			Message = message,
			GraphicsNote = note,
			GamePath = install.GamePath,
			IncludesPassword = url.Contains("+password", StringComparison.Ordinal),
			Applied = applied
		};
	}

	public static async Task<ZomboidLaunchPlan> PrepareAsync(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath, string feedJson, HttpClient http, CancellationToken ct, string localPassword = "", int ramMb = 0, bool saveMemory = true)
	{
		ZomboidLaunchPlan plan = Prepare(sources, userProfile, launcherHome, minecraftLockPath, feedJson, localPassword, ramMb, saveMemory);
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
				GamePath = plan.GamePath,
				Applied = plan.Applied
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
		if (SteamAppLaunch.TryBuild(url, SteamRegistry.InstallPaths(), File.Exists, out string steamExe, out IReadOnlyList<string> arguments))
		{
			ProcessStartInfo info = new ProcessStartInfo(steamExe)
			{
				UseShellExecute = false
			};
			for (int i = 0; i < arguments.Count; i++)
			{
				info.ArgumentList.Add(arguments[i]);
			}
			Process.Start(info);
			return;
		}
		Process.Start(new ProcessStartInfo(url)
		{
			UseShellExecute = true
		});
	}
}

/// <summary>
/// Current Steam clients can drop the arguments after "//" in a steam://rungameid link, so the game opens
/// to its main menu without +connect. When a run link carries arguments, start steam.exe -applaunch instead,
/// which hands the arguments to the game. Links without arguments keep using the steam:// protocol.
/// </summary>
public static class SteamAppLaunch
{
	private const string Prefix = "steam://rungameid/";

	public static bool TryBuild(string url, IReadOnlyList<string> steamRoots, Func<string, bool> fileExists, out string steamExe, out IReadOnlyList<string> arguments)
	{
		steamExe = "";
		arguments = Array.Empty<string>();
		if (string.IsNullOrEmpty(url) || !url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || steamRoots == null || fileExists == null)
		{
			return false;
		}
		string rest = url.Substring(Prefix.Length);
		int split = rest.IndexOf("//", StringComparison.Ordinal);
		if (split <= 0)
		{
			return false;
		}
		string appId = rest.Substring(0, split);
		for (int i = 0; i < appId.Length; i++)
		{
			if (appId[i] < '0' || appId[i] > '9')
			{
				return false;
			}
		}
		string tail = Uri.UnescapeDataString(rest.Substring(split + 2)).Trim().TrimEnd('/');
		string[] parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return false;
		}
		for (int i = 0; i < steamRoots.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(steamRoots[i]))
			{
				continue;
			}
			string candidate = Path.Combine(steamRoots[i].Trim().Replace('/', '\\'), "steam.exe");
			bool exists;
			try
			{
				exists = fileExists(candidate);
			}
			catch (Exception)
			{
				exists = false;
			}
			if (!exists)
			{
				continue;
			}
			List<string> list = new List<string> { "-applaunch", appId };
			list.AddRange(parts);
			steamExe = candidate;
			arguments = list;
			return true;
		}
		return false;
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

public static class ZomboidPasswordStore
{
	public static string PathFor(string launcherHome)
	{
		return Path.Combine(launcherHome, "zomboid-password.txt");
	}

	public static bool Exists(string launcherHome)
	{
		return File.Exists(PathFor(launcherHome));
	}

	public static string Load(string launcherHome)
	{
		try
		{
			return Exists(launcherHome) ? File.ReadAllText(PathFor(launcherHome)).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal) : "";
		}
		catch (IOException)
		{
			return "";
		}
	}

	public static void Save(string launcherHome, string password)
	{
		Directory.CreateDirectory(launcherHome);
		File.WriteAllText(PathFor(launcherHome), password ?? "");
	}
}
