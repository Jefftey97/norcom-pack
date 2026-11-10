using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class DiscoveredSetting
{
	public string Key { get; init; } = "";

	public string Label { get; init; } = "";

	public string Help { get; init; } = "";

	public string Value { get; init; } = "";

	public string Kind { get; init; } = "text";

	public bool ReadOnly { get; init; }

	public double Min { get; init; }

	public double Max { get; init; }

	public double Step { get; init; } = 1;
}

public sealed class DiscoveredModFile
{
	public string RelativePath { get; init; } = "";

	public string Format { get; init; } = "unknown";

	public bool Preserved { get; init; }

	public bool ReadOnly { get; init; }

	public string Note { get; init; } = "";

	public IReadOnlyList<DiscoveredSetting> Settings { get; init; } = Array.Empty<DiscoveredSetting>();
}

public static class ModSettingsCatalog
{
	public const string ManagedByPack = "Managed by the pack. Play replaces this file, so it cannot be edited here.";

	public static IReadOnlyList<DiscoveredModFile> Discover(string configRoot, string indexToml, string schemaJson)
	{
		ReadPackIndex(indexToml, out HashSet<string> indexed, out HashSet<string> preserved);
		Dictionary<string, List<SchemaSetting>> schema = ReadSchema(schemaJson);
		List<DiscoveredModFile> found = new List<DiscoveredModFile>();
		if (!Directory.Exists(configRoot))
		{
			return found;
		}
		foreach (string path in Directory.EnumerateFiles(configRoot, "*", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(configRoot, path).Replace('\\', '/');
			if (relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathRooted(relative))
			{
				continue;
			}
			if (relative.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("norcom-settings-schema.json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			bool keep = preserved.Contains("config/" + relative) || preserved.Contains(relative);
			bool managed = (indexed.Contains("config/" + relative) || indexed.Contains(relative)) && !keep;
			string extension = Path.GetExtension(path);
			schema.TryGetValue(relative, out List<SchemaSetting> hints);
			if (hints == null)
			{
				schema.TryGetValue(Path.GetFileName(path), out hints);
			}
			DiscoveredModFile file;
			if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
			{
				file = ReadJson(path, relative, keep, hints);
			}
			else if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase))
			{
				file = ReadToml(path, relative, keep, hints);
			}
			else if (extension.Equals(".properties", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
			{
				file = ReadLines(path, relative, extension.TrimStart('.').ToLowerInvariant(), keep, hints);
			}
			else
			{
				file = new DiscoveredModFile
				{
					RelativePath = relative,
					Format = "unknown",
					Preserved = keep,
					ReadOnly = true,
					Note = "This format is read-only. NORCOM does not guess how to edit it.",
					Settings = Array.Empty<DiscoveredSetting>()
				};
			}
			found.Add(LockIfManaged(file, managed));
		}
		found.Sort((DiscoveredModFile left, DiscoveredModFile right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
		return found;
	}

	public static bool TrySave(string configRoot, DiscoveredModFile file, IReadOnlyDictionary<string, string> edits, out string error)
	{
		error = "";
		if (GameSession.IsRunning("minecraft", Norcom.Launcher.Core.NorcomPaths.GameLockPath) || GameSession.IsRunning("zomboid", Norcom.Launcher.Core.NorcomPaths.GameLockPath))
		{
			error = "The game is running. Settings were not written.";
			return false;
		}
		if (file.ReadOnly || file.Format == "unknown")
		{
			error = file.Note.Length > 0 ? file.Note : "That file is read-only.";
			return false;
		}
		string path;
		try
		{
			path = PackSyncService.ConfinedPath(configRoot, file.RelativePath);
		}
		catch (InvalidDataException ex)
		{
			error = ex.Message;
			return false;
		}
		if (!File.Exists(path))
		{
			error = "That file is no longer there.";
			return false;
		}
		string original;
		try
		{
			original = File.ReadAllText(path);
		}
		catch (IOException ex)
		{
			error = ex.Message;
			return false;
		}
		string updated;
		if (file.Format == "json")
		{
			if (!TryPatchJson(original, edits, out updated, out error))
			{
				return false;
			}
		}
		else if (file.Format == "toml")
		{
			if (!TomlValuePatch.TryApply(original, edits, out updated, out error))
			{
				return false;
			}
		}
		else if (!TryPatchProperties(original, edits, out updated, out error))
		{
			return false;
		}
		try
		{
			GameSettingsWriter.BackupOnce(path);
			PackSyncService.WriteContainedText(configRoot, file.RelativePath, updated);
		}
		catch (InvalidDataException ex)
		{
			error = ex.Message;
			return false;
		}
		catch (IOException ex)
		{
			error = ex.Message;
			return false;
		}
		return true;
	}

	public static IReadOnlyList<DiscoveredModFile> DiscoverFeed(string configRoot, string indexToml, string schemaJson)
	{
		ReadPackIndex(indexToml, out HashSet<string> indexed, out _);
		if (indexed.Count == 0)
		{
			return Array.Empty<DiscoveredModFile>();
		}
		IReadOnlyList<DiscoveredModFile> all = Discover(configRoot, indexToml, schemaJson);
		List<DiscoveredModFile> listed = new List<DiscoveredModFile>();
		for (int i = 0; i < all.Count; i++)
		{
			DiscoveredModFile file = all[i];
			if (indexed.Contains("config/" + file.RelativePath) || indexed.Contains(file.RelativePath))
			{
				listed.Add(file);
			}
		}
		return listed;
	}

	public static IReadOnlyList<DiscoveredModFile> DiscoverZomboidFeed(string modsRoot, ZomboidFeedDoc feed, string schemaJson)
	{
		List<DiscoveredModFile> found = new List<DiscoveredModFile>();
		if (feed == null)
		{
			return found;
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		IReadOnlyList<ZomboidWorkshopMod> workshop = feed.Workshop ?? Array.Empty<ZomboidWorkshopMod>();
		for (int i = 0; i < workshop.Count; i++)
		{
			string modId = workshop[i].ModId ?? "";
			if (!SafeName(modId) || !seen.Add(modId))
			{
				continue;
			}
			string dir = Path.Combine(modsRoot, modId);
			if (!Directory.Exists(dir))
			{
				found.Add(new DiscoveredModFile
				{
					RelativePath = modId,
					Format = "unknown",
					ReadOnly = true,
					Note = "This pack mod is not on this PC yet."
				});
				continue;
			}
			string schemaPath = Path.Combine(dir, "norcom-settings-schema.json");
			string schema = File.Exists(schemaPath) ? File.ReadAllText(schemaPath) : schemaJson;
			IReadOnlyList<DiscoveredModFile> inner = Discover(dir, "", schema ?? "");
			if (inner.Count == 0)
			{
				found.Add(new DiscoveredModFile
				{
					RelativePath = modId,
					Format = "unknown",
					ReadOnly = true,
					Note = "This pack mod has no settings file yet."
				});
				continue;
			}
			for (int f = 0; f < inner.Count; f++)
			{
				seen.Add(modId + "/" + inner[f].RelativePath);
				found.Add(Prefix(modId, inner[f]));
			}
		}
		IReadOnlyList<ZomboidPackFile> files = feed.Files ?? Array.Empty<ZomboidPackFile>();
		for (int i = 0; i < files.Count; i++)
		{
			string path = (files[i].Path ?? "").Replace('\\', '/');
			if (!path.StartsWith("mods/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
			{
				continue;
			}
			string relative = path.Substring("mods/".Length);
			if (!SafeRelative(relative) || !seen.Add(relative))
			{
				continue;
			}
			string full = Path.Combine(modsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(full))
			{
				found.Add(new DiscoveredModFile
				{
					RelativePath = relative,
					Format = "unknown",
					ReadOnly = true,
					Note = "This pack file is not on this PC yet."
				});
				continue;
			}
			string parent = Path.GetDirectoryName(full) ?? modsRoot;
			IReadOnlyList<DiscoveredModFile> one = Discover(parent, "", schemaJson ?? "");
			DiscoveredModFile match = null;
			string name = Path.GetFileName(relative);
			for (int f = 0; f < one.Count; f++)
			{
				if (one[f].RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					match = one[f];
					break;
				}
			}
			found.Add(match == null
				? new DiscoveredModFile { RelativePath = relative, Format = "unknown", ReadOnly = true, Note = "This format is read-only. NORCOM does not guess how to edit it." }
				: Prefix(Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "", match));
		}
		return found;
	}

	private static DiscoveredModFile Prefix(string modId, DiscoveredModFile file)
	{
		string relative = string.IsNullOrEmpty(modId) ? file.RelativePath : modId + "/" + file.RelativePath;
		return new DiscoveredModFile
		{
			RelativePath = relative,
			Format = file.Format,
			Preserved = file.Preserved,
			ReadOnly = file.ReadOnly,
			Note = file.Note,
			Settings = file.Settings
		};
	}

	private static bool SafeName(string name)
	{
		return SafeRelative(name) && name.IndexOf('/') < 0;
	}

	private static bool SafeRelative(string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
		{
			return false;
		}
		return relative.IndexOfAny(new char[2] { ':', '\\' }) < 0;
	}

	public static HashSet<string> PreservedPaths(string indexToml)
	{
		ReadPackIndex(indexToml, out _, out HashSet<string> preserved);
		return preserved;
	}

	public static void ReadPackIndex(string indexToml, out HashSet<string> indexed, out HashSet<string> preserved)
	{
		indexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		preserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(indexToml))
		{
			return;
		}
		TomlLite.Doc doc = TomlLite.Parse(indexToml);
		for (int i = 0; i < doc.Files.Count; i++)
		{
			Dictionary<string, string> row = doc.Files[i];
			if (!row.TryGetValue("file", out string relative) || string.IsNullOrWhiteSpace(relative))
			{
				continue;
			}
			string path = relative.Replace('\\', '/');
			indexed.Add(path);
			if (row.TryGetValue("preserve", out string preserve) && preserve.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				preserved.Add(path);
			}
		}
	}

	private static DiscoveredModFile LockIfManaged(DiscoveredModFile file, bool managed)
	{
		if (!managed)
		{
			return file;
		}
		return new DiscoveredModFile
		{
			RelativePath = file.RelativePath,
			Format = file.Format,
			Preserved = false,
			ReadOnly = true,
			Note = ManagedByPack,
			Settings = file.Settings
		};
	}

	private static bool TryPatchJson(string original, IReadOnlyDictionary<string, string> edits, out string updated, out string error)
	{
		updated = original;
		error = "";
		JsonNode root;
		try
		{
			root = JsonNode.Parse(original);
		}
		catch (JsonException ex)
		{
			error = "That JSON could not be read, so it was left unchanged. " + ex.Message;
			return false;
		}
		if (root is not JsonObject)
		{
			error = "That JSON could not be read, so it was left unchanged.";
			return false;
		}
		foreach (KeyValuePair<string, string> edit in edits)
		{
			SetDotted(root, edit.Key, edit.Value);
		}
		updated = root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		});
		return true;
	}

	private static bool TryPatchProperties(string original, IReadOnlyDictionary<string, string> edits, out string updated, out string error)
	{
		updated = original ?? "";
		error = "";
		string[] lines = (original ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
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
				error = "That properties file could not be read, so it was left unchanged.";
				return false;
			}
		}
		updated = OptionsPatch.ApplyProperties(original, new Dictionary<string, string>(edits, StringComparer.OrdinalIgnoreCase));
		return true;
	}

	private static DiscoveredModFile ReadJson(string path, string relative, bool preserved, List<SchemaSetting> hints)
	{
		List<DiscoveredSetting> settings = new List<DiscoveredSetting>();
		try
		{
			JsonNode root = JsonNode.Parse(File.ReadAllText(path));
			if (root != null)
			{
				Walk(root, "", settings, hints);
			}
		}
		catch (JsonException)
		{
			return new DiscoveredModFile
			{
				RelativePath = relative,
				Format = "json",
				Preserved = preserved,
				ReadOnly = true,
				Note = "This JSON could not be read, so it was left unchanged.",
				Settings = Array.Empty<DiscoveredSetting>()
			};
		}
		return new DiscoveredModFile
		{
			RelativePath = relative,
			Format = "json",
			Preserved = preserved,
			ReadOnly = false,
			Note = preserved ? "Pack updates will not replace this file. Only the keys you change here are written." : "",
			Settings = settings
		};
	}

	private static void Walk(JsonNode node, string prefix, List<DiscoveredSetting> settings, List<SchemaSetting> hints)
	{
		if (node is JsonObject obj)
		{
			foreach (KeyValuePair<string, JsonNode> pair in obj)
			{
				string path = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
				if (pair.Value is JsonObject or JsonArray)
				{
					if (pair.Value is JsonArray)
					{
						settings.Add(Described(path, "", "list", readOnly: true, hints));
					}
					else
					{
						Walk(pair.Value, path, settings, hints);
					}
				}
				else
				{
					settings.Add(Described(path, Scalar(pair.Value), "text", readOnly: false, hints));
				}
			}
		}
	}

	private static DiscoveredModFile ReadToml(string path, string relative, bool preserved, List<SchemaSetting> hints)
	{
		List<DiscoveredSetting> settings = new List<DiscoveredSetting>();
		string section = "";
		bool arrayTable = false;
		string[] lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			if (!TomlValuePatch.TryInspect(lines[i], section, arrayTable, out string key, out string value, out bool inline, out string nextSection, out bool nextArray, out string error))
			{
				return Unreadable(relative, "toml", preserved, "This TOML could not be read, so it was left unchanged.");
			}
			if (key.Length == 0)
			{
				section = nextSection;
				arrayTable = nextArray;
				continue;
			}
			string shown = value.Trim().Trim('"');
			settings.Add(Described(key, shown, inline || arrayTable ? "inline" : "text", readOnly: inline || arrayTable, hints));
			section = nextSection;
			arrayTable = nextArray;
		}
		return new DiscoveredModFile
		{
			RelativePath = relative,
			Format = "toml",
			Preserved = preserved,
			ReadOnly = false,
			Note = preserved ? "Pack updates will not replace this file. Only the keys you change here are written." : "",
			Settings = settings
		};
	}

	private static DiscoveredModFile Unreadable(string relative, string format, bool preserved, string note)
	{
		return new DiscoveredModFile
		{
			RelativePath = relative,
			Format = format,
			Preserved = preserved,
			ReadOnly = true,
			Note = note,
			Settings = Array.Empty<DiscoveredSetting>()
		};
	}

	private static DiscoveredModFile ReadLines(string path, string relative, string format, bool preserved, List<SchemaSetting> hints)
	{
		List<DiscoveredSetting> settings = new List<DiscoveredSetting>();
		string section = "";
		string[] lines = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
			{
				continue;
			}
			if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
			{
				section = trimmed.Substring(1, trimmed.Length - 2).Trim();
				continue;
			}
			int split = trimmed.IndexOf('=');
			if (split <= 0)
			{
				return Unreadable(relative, format, preserved, "This file could not be read, so it was left unchanged.");
			}
			string key = trimmed.Substring(0, split).Trim();
			string value = trimmed.Substring(split + 1).Trim();
			if (section.Length > 0)
			{
				key = section + "." + key;
			}
			settings.Add(Described(key, value, "text", readOnly: false, hints));
		}
		return new DiscoveredModFile
		{
			RelativePath = relative,
			Format = format,
			Preserved = preserved,
			ReadOnly = false,
			Note = preserved ? "Pack updates will not replace this file. Only the keys you change here are written." : "",
			Settings = settings
		};
	}

	private static DiscoveredSetting Described(string key, string value, string fallbackKind, bool readOnly, List<SchemaSetting> hints)
	{
		SchemaSetting hint = null;
		if (hints != null)
		{
			for (int i = 0; i < hints.Count; i++)
			{
				if (string.Equals(hints[i].Key, key, StringComparison.OrdinalIgnoreCase))
				{
					hint = hints[i];
					break;
				}
			}
		}
		if (hint == null)
		{
			return new DiscoveredSetting
			{
				Key = key,
				Label = key,
				Help = "",
				Value = value,
				Kind = fallbackKind,
				ReadOnly = readOnly || fallbackKind == "list"
			};
		}
		return new DiscoveredSetting
		{
			Key = key,
			Label = string.IsNullOrWhiteSpace(hint.Label) ? key : hint.Label,
			Help = hint.Help ?? "",
			Value = value,
			Kind = string.IsNullOrWhiteSpace(hint.Kind) ? fallbackKind : hint.Kind,
			ReadOnly = readOnly,
			Min = hint.Min,
			Max = hint.Max,
			Step = hint.Step <= 0 ? 1 : hint.Step
		};
	}

	private static Dictionary<string, List<SchemaSetting>> ReadSchema(string schemaJson)
	{
		Dictionary<string, List<SchemaSetting>> map = new Dictionary<string, List<SchemaSetting>>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(schemaJson))
		{
			return map;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(schemaJson);
			if (!document.RootElement.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array)
			{
				return map;
			}
			foreach (JsonElement file in files.EnumerateArray())
			{
				string path = file.TryGetProperty("path", out JsonElement pathValue) ? pathValue.GetString() ?? "" : "";
				if (path.Length == 0)
				{
					continue;
				}
				List<SchemaSetting> settings = new List<SchemaSetting>();
				if (file.TryGetProperty("settings", out JsonElement settingValues) && settingValues.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement setting in settingValues.EnumerateArray())
					{
						settings.Add(new SchemaSetting
						{
							Key = setting.TryGetProperty("key", out JsonElement key) ? key.GetString() ?? "" : "",
							Label = setting.TryGetProperty("label", out JsonElement label) ? label.GetString() ?? "" : "",
							Help = setting.TryGetProperty("help", out JsonElement help) ? help.GetString() ?? "" : "",
							Kind = setting.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() ?? "" : "",
							Min = setting.TryGetProperty("min", out JsonElement min) && min.TryGetDouble(out double minValue) ? minValue : 0,
							Max = setting.TryGetProperty("max", out JsonElement max) && max.TryGetDouble(out double maxValue) ? maxValue : 0,
							Step = setting.TryGetProperty("step", out JsonElement step) && step.TryGetDouble(out double stepValue) ? stepValue : 1
						});
					}
				}
				map[path.Replace('\\', '/')] = settings;
			}
		}
		catch (JsonException)
		{
			return map;
		}
		return map;
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
		JsonNode existing = current[leaf];
		if (existing is JsonValue previous && previous.TryGetValue<int>(out _) && int.TryParse(value, out int whole))
		{
			current[leaf] = JsonValue.Create(whole);
			return;
		}
		if (existing is JsonValue flag && flag.TryGetValue<bool>(out _) && bool.TryParse(value, out bool parsed))
		{
			current[leaf] = JsonValue.Create(parsed);
			return;
		}
		current[leaf] = JsonValue.Create(value);
	}

	private static string Scalar(JsonNode node)
	{
		if (node is JsonValue value && value.TryGetValue<string>(out string text))
		{
			return text;
		}
		return node == null ? "" : node.ToJsonString();
	}

	private sealed class SchemaSetting
	{
		public string Key { get; init; } = "";

		public string Label { get; init; } = "";

		public string Help { get; init; } = "";

		public string Kind { get; init; } = "";

		public double Min { get; init; }

		public double Max { get; init; }

		public double Step { get; init; } = 1;
	}
}
