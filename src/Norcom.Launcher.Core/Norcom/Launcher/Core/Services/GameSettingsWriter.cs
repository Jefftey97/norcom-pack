using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norcom.Launcher.Core.Updates;

namespace Norcom.Launcher.Core.Services;

public static class GameSession
{
	public static Func<string, bool>? Probe { get; set; }

	public static bool IsRunning(string gameId, string minecraftLockPath)
	{
		if (Probe != null)
		{
			return Probe(gameId);
		}
		try
		{
			if (string.Equals(gameId, "minecraft", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(minecraftLockPath) && GameUpdateGuard.IsBlocked(minecraftLockPath))
			{
				return true;
			}
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			if (string.Equals(gameId, "zomboid", StringComparison.OrdinalIgnoreCase))
			{
				return System.Diagnostics.Process.GetProcessesByName("ProjectZomboid64").Length > 0;
			}
			return false;
		}
		catch (Exception)
		{
			return true;
		}
	}
}

public static class GameLaunchGraphics
{
	public static string ApplyBeforeLaunch(string gameId, string gameDir, string launcherHome, string minecraftLockPath, string userProfile)
	{
		GameDefinition game = GameGraphicsCatalog.Require(gameId);
		GraphicsProfileStore store = GraphicsProfileStore.Load(launcherHome);
		SavedGraphicsProfile saved = store.Find(gameId);
		MachineSpecs specs = MachineSpecsProbe.Read();
		PresetChoice recommended = GameGraphicsCatalog.Recommend(game, specs);
		if (saved == null || saved.Auto)
		{
			saved = new SavedGraphicsProfile
			{
				Preset = recommended.Preset,
				Auto = true,
				Reason = recommended.Reason
			};
			store.Put(gameId, saved);
			store.Save(launcherHome);
		}
		if (GameSession.IsRunning(gameId, minecraftLockPath))
		{
			return "Skipped " + game.Name + " graphics because the game is running.";
		}
		return GameSettingsWriter.Apply(game, saved, gameDir, userProfile);
	}
}

public static class GameSettingsWriter
{
	public static string Apply(GameDefinition game, SavedGraphicsProfile profile, string gameDir, string userProfile)
	{
		Dictionary<string, string> values = GameGraphicsCatalog.EffectiveValues(game, profile);
		List<string> wrote = new List<string>();
		if (string.Equals(game.Id, "minecraft", StringComparison.OrdinalIgnoreCase))
		{
			string optionsError = WriteOptions(gameDir, ValuesFor(game, values, "options"), wrote);
			if (optionsError.Length > 0)
			{
				return optionsError;
			}
			if (ModInstalled(gameDir, "sodium"))
			{
				string sodiumError = WriteJson(gameDir, "config/sodium-options.json", ValuesFor(game, values, "sodium"), wrote);
				if (sodiumError.Length > 0)
				{
					return sodiumError;
				}
			}
			if (ModInstalled(gameDir, "iris"))
			{
				string irisError = WriteProperties(gameDir, "config/iris.properties", ValuesFor(game, values, "iris"), wrote);
				if (irisError.Length > 0)
				{
					return irisError;
				}
			}
		}
		else if (string.Equals(game.Id, "zomboid", StringComparison.OrdinalIgnoreCase))
		{
			string folder = Path.Combine(userProfile, "Zomboid");
			if (!Directory.Exists(folder))
			{
				return "Project Zomboid settings were not written because " + folder + " does not exist.";
			}
			string zomboidError = WriteProperties(folder, "options.ini", ValuesFor(game, values, "zomboid"), wrote);
			if (zomboidError.Length > 0)
			{
				return zomboidError;
			}
		}
		if (wrote.Count == 0)
		{
			return game.Name + " " + profile.Preset + " had no config files to update.";
		}
		return game.Name + " " + profile.Preset + " applied to " + string.Join(", ", wrote) + ".";
	}

	private static Dictionary<string, string> ValuesFor(GameDefinition game, Dictionary<string, string> values, string target)
	{
		Dictionary<string, string> picked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < game.Settings.Length; i++)
		{
			GraphicsSetting setting = game.Settings[i];
			if (!string.Equals(setting.Target, target, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (values.TryGetValue(setting.Key, out string value))
			{
				picked[setting.Key] = value;
			}
		}
		return picked;
	}

	private static string WriteOptions(string gameDir, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0)
		{
			return "";
		}
		Directory.CreateDirectory(gameDir);
		if (!TryReadContained(gameDir, "options.txt", out string existing, out string readError))
		{
			return "options.txt was left unchanged. " + readError;
		}
		if (!TryOptions(existing ?? "", values, out string updated, out string error))
		{
			return "options.txt was left unchanged. " + error;
		}
		return Commit(gameDir, "options.txt", updated, "options.txt", wrote);
	}

	private static string WriteProperties(string root, string relative, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0)
		{
			return "";
		}
		if (!TryReadContained(root, relative, out string existing, out string readError))
		{
			return relative + " was left unchanged. " + readError;
		}
		if (!TryProperties(existing ?? "", values, out string updated, out string error))
		{
			return relative + " was left unchanged. " + error;
		}
		return Commit(root, relative, updated, Path.GetFileName(relative.Replace('\\', '/')), wrote);
	}

	private static string WriteJson(string root, string relative, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0)
		{
			return "";
		}
		if (!TryReadContained(root, relative, out string existing, out string readError))
		{
			return relative + " was left unchanged. " + readError;
		}
		if (existing == null)
		{
			return "";
		}
		JsonNode rootNode;
		try
		{
			rootNode = JsonNode.Parse(existing);
		}
		catch (JsonException)
		{
			return relative + " was left unchanged. That JSON could not be read.";
		}
		if (rootNode is not JsonObject)
		{
			return relative + " was left unchanged. That JSON could not be read.";
		}
		foreach (KeyValuePair<string, string> pair in values)
		{
			SetDotted(rootNode, pair.Key, pair.Value);
		}
		string updated = rootNode.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		});
		return Commit(root, relative, updated, Path.GetFileName(relative.Replace('\\', '/')), wrote);
	}

	private static bool TryReadContained(string root, string relative, out string text, out string error)
	{
		text = null;
		error = "";
		string path;
		try
		{
			path = PackSyncService.ConfinedPath(root, relative);
		}
		catch (InvalidDataException ex)
		{
			error = ex.Message;
			return false;
		}
		if (!File.Exists(path))
		{
			return true;
		}
		try
		{
			text = File.ReadAllText(path);
			return true;
		}
		catch (IOException ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static string Commit(string root, string relative, string updated, string label, List<string> wrote)
	{
		try
		{
			string path = PackSyncService.ConfinedPath(root, relative);
			BackupOnce(path);
			PackSyncService.WriteContainedText(root, relative, updated);
		}
		catch (InvalidDataException ex)
		{
			return label + " was left unchanged. " + ex.Message;
		}
		catch (IOException ex)
		{
			return label + " was left unchanged. " + ex.Message;
		}
		wrote.Add(label);
		return "";
	}

	private static bool TryOptions(string existing, Dictionary<string, string> values, out string updated, out string error)
	{
		updated = "";
		error = "";
		string[] lines = (existing ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}
			if (trimmed.IndexOf(':') <= 0)
			{
				error = "That options file could not be read.";
				return false;
			}
		}
		updated = OptionsPatch.Apply(existing, values);
		return true;
	}

	private static bool TryProperties(string existing, Dictionary<string, string> values, out string updated, out string error)
	{
		updated = "";
		error = "";
		string[] lines = (existing ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
			{
				continue;
			}
			if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
			{
				continue;
			}
			if (trimmed.IndexOf('=') <= 0)
			{
				error = "That properties file could not be read.";
				return false;
			}
		}
		updated = OptionsPatch.ApplyProperties(existing, values);
		return true;
	}

	private static void SetDotted(JsonNode root, string path, string value)
	{
		JsonObject current = root as JsonObject ?? new JsonObject();
		string[] parts = path.Split('.');
		for (int i = 0; i < parts.Length - 1; i++)
		{
			if (current[parts[i]] is not JsonObject child)
			{
				child = new JsonObject();
				current[parts[i]] = child;
			}
			current = child;
		}
		string leaf = parts[parts.Length - 1];
		current[leaf] = Coerce(current[leaf], value);
	}

	private static JsonNode Coerce(JsonNode existing, string value)
	{
		if (existing is JsonValue previous)
		{
			if (previous.TryGetValue<bool>(out _))
			{
				return JsonValue.Create(value.Equals("true", StringComparison.OrdinalIgnoreCase));
			}
			if (previous.TryGetValue<int>(out _) && int.TryParse(value, out int whole))
			{
				return JsonValue.Create(whole);
			}
		}
		if (bool.TryParse(value, out bool flag) && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase)))
		{
			return JsonValue.Create(flag);
		}
		if (int.TryParse(value, out int number) && value.IndexOf('.') < 0)
		{
			return JsonValue.Create(number);
		}
		return JsonValue.Create(value);
	}

	public static bool ModInstalled(string gameDir, string token)
	{
		string configName = token.Equals("sodium", StringComparison.OrdinalIgnoreCase) ? Path.Combine(gameDir, "config", "sodium-options.json") : Path.Combine(gameDir, "config", "iris.properties");
		if (File.Exists(configName))
		{
			return true;
		}
		string mods = Path.Combine(gameDir, "mods");
		if (!Directory.Exists(mods))
		{
			return false;
		}
		foreach (string jar in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
		{
			string name = Path.GetFileName(jar);
			if (token.Equals("sodium", StringComparison.OrdinalIgnoreCase))
			{
				if (name.Contains("sodium", StringComparison.OrdinalIgnoreCase) && !name.Contains("reeses", StringComparison.OrdinalIgnoreCase) && !name.Contains("sodium-extra", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			else if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public static void BackupOnce(string path)
	{
		if (!File.Exists(path))
		{
			return;
		}
		string backup = path + ".norcom-backup";
		if (!File.Exists(backup))
		{
			File.Copy(path, backup);
		}
	}
}
