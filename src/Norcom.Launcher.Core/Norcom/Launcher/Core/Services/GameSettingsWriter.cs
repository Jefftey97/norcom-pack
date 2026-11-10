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
			WriteOptions(gameDir, ValuesFor(game, values, "options"), wrote);
			if (ModInstalled(gameDir, "sodium"))
			{
				WriteJson(Path.Combine(gameDir, "config", "sodium-options.json"), ValuesFor(game, values, "sodium"), wrote);
			}
			if (ModInstalled(gameDir, "iris"))
			{
				WriteProperties(Path.Combine(gameDir, "config", "iris.properties"), ValuesFor(game, values, "iris"), wrote);
			}
		}
		else if (string.Equals(game.Id, "zomboid", StringComparison.OrdinalIgnoreCase))
		{
			string folder = Path.Combine(userProfile, "Zomboid");
			if (!Directory.Exists(folder))
			{
				return "Project Zomboid settings were not written because " + folder + " does not exist.";
			}
			WriteProperties(Path.Combine(folder, "options.ini"), ValuesFor(game, values, "zomboid"), wrote);
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

	private static void WriteOptions(string gameDir, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0)
		{
			return;
		}
		Directory.CreateDirectory(gameDir);
		string path = Path.Combine(gameDir, "options.txt");
		BackupOnce(path);
		string existing = File.Exists(path) ? File.ReadAllText(path) : "";
		File.WriteAllText(path, OptionsPatch.Apply(existing, values));
		wrote.Add("options.txt");
	}

	private static void WriteProperties(string path, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0)
		{
			return;
		}
		string folder = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(folder))
		{
			Directory.CreateDirectory(folder);
		}
		BackupOnce(path);
		string existing = File.Exists(path) ? File.ReadAllText(path) : "";
		File.WriteAllText(path, OptionsPatch.ApplyProperties(existing, values));
		wrote.Add(Path.GetFileName(path));
	}

	private static void WriteJson(string path, Dictionary<string, string> values, List<string> wrote)
	{
		if (values.Count == 0 || !File.Exists(path))
		{
			return;
		}
		BackupOnce(path);
		JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
		foreach (KeyValuePair<string, string> pair in values)
		{
			SetDotted(root, pair.Key, pair.Value);
		}
		File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		wrote.Add(Path.GetFileName(path));
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
