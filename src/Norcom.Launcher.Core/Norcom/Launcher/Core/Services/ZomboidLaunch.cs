using System;
using System.Diagnostics;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public sealed class ZomboidLaunchPlan
{
	public bool CanStart { get; init; }

	public string Url { get; init; } = ZomboidLaunch.SteamUrl;

	public string Message { get; init; } = "";

	public string GraphicsNote { get; init; } = "";

	public string GamePath { get; init; } = "";
}

public static class ZomboidLaunch
{
	public const string SteamUrl = "steam://rungameid/108600";

	// TODO(signed-address): append Steam +connect when the NORCOM server address is a signed channel or pack field.
	// Do not read launcher/channel-v1.json for that. Until the field exists, the home screen only copies the address.

	public static ZomboidLaunchPlan Prepare(SteamSources sources, string userProfile, string launcherHome, string minecraftLockPath)
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
		string note = "";
		try
		{
			note = GameLaunchGraphics.ApplyBeforeLaunch("zomboid", install.GamePath, launcherHome, minecraftLockPath, userProfile);
		}
		catch (Exception ex)
		{
			note = "Zomboid graphics were not applied (" + ex.GetType().Name + ").";
		}
		return new ZomboidLaunchPlan
		{
			CanStart = true,
			Url = SteamUrl,
			Message = "Opening Project Zomboid in Steam. Workshop mods are not managed by NORCOM.",
			GraphicsNote = note,
			GamePath = install.GamePath
		};
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
