using System;
using System.IO;
using System.Text.Json;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class UpdateNotes
{
	public static void Remember(string launcherDir, LauncherManifest manifest, string localVersion)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(launcherDir) || string.IsNullOrWhiteSpace(manifest.Notes))
			{
				return;
			}
			Directory.CreateDirectory(launcherDir);
			string json = JsonSerializer.Serialize(new Stored(manifest.Version.Trim(), manifest.Notes.Trim(), localVersion.Trim()));
			File.WriteAllText(Path.Combine(launcherDir, "update-notes.json"), json);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("update notes " + ex.GetType().Name);
		}
	}

	public static string? Section(string launcherDir, string localVersion)
	{
		try
		{
			string path = Path.Combine(launcherDir, "update-notes.json");
			if (!File.Exists(path))
			{
				return null;
			}
			Stored? stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
			if (stored == null || string.IsNullOrWhiteSpace(stored.Notes))
			{
				return null;
			}
			bool available = VersionCompare.Compare(stored.Version, localVersion) > 0;
			string heading = available ? "Update " + stored.Version + " is available." : "Launcher update notes";
			return heading + Environment.NewLine + stored.Notes.Trim();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return null;
		}
	}

	private sealed record Stored(string Version, string Notes, string LocalVersion);
}
