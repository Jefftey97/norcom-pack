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
	public static IReadOnlyList<DiscoveredModFile> Discover(string configRoot, string indexToml, string schemaJson)
	{
		HashSet<string> preserved = PreservedPaths(indexToml);
		Dictionary<string, List<SchemaSetting>> schema = ReadSchema(schemaJson);
		List<DiscoveredModFile> found = new List<DiscoveredModFile>();
		if (!Directory.Exists(configRoot))
		{
			return found;
		}
		foreach (string path in Directory.EnumerateFiles(configRoot, "*", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(configRoot, path).Replace('\\', '/');
			if (relative.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("norcom-settings-schema.json", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			bool keep = preserved.Contains("config/" + relative) || preserved.Contains(relative);
			string extension = Path.GetExtension(path);
			schema.TryGetValue(relative, out List<SchemaSetting> hints);
			if (hints == null)
			{
				schema.TryGetValue(Path.GetFileName(path), out hints);
			}
			if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
			{
				found.Add(ReadJson(path, relative, keep, hints));
			}
			else if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase))
			{
				found.Add(ReadLines(path, relative, "toml", keep, hints, '='));
			}
			else if (extension.Equals(".properties", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))
			{
				found.Add(ReadLines(path, relative, extension.TrimStart('.').ToLowerInvariant(), keep, hints, '='));
			}
			else
			{
				found.Add(new DiscoveredModFile
				{
					RelativePath = relative,
					Format = "unknown",
					Preserved = keep,
					ReadOnly = true,
					Note = "This format is listed only. NORCOM does not guess how to edit it.",
					Settings = Array.Empty<DiscoveredSetting>()
				});
			}
		}
		found.Sort((DiscoveredModFile left, DiscoveredModFile right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));
		return found;
	}

	public static bool TrySave(string configRoot, DiscoveredModFile file, IReadOnlyDictionary<string, string> edits, out string error)
	{
		error = "";
		if (file.ReadOnly || file.Format == "unknown")
		{
			error = "That file is read-only.";
			return false;
		}
		string path = Path.Combine(configRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(path))
		{
			error = "That file is no longer there.";
			return false;
		}
		GameSettingsWriter.BackupOnce(path);
		if (file.Format == "json")
		{
			JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
			foreach (KeyValuePair<string, string> edit in edits)
			{
				SetDotted(root, edit.Key, edit.Value);
			}
			File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			return true;
		}
		Dictionary<string, string> updates = new Dictionary<string, string>(edits, StringComparer.OrdinalIgnoreCase);
		File.WriteAllText(path, OptionsPatch.ApplyProperties(File.ReadAllText(path), updates));
		return true;
	}

	public static HashSet<string> PreservedPaths(string indexToml)
	{
		HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(indexToml))
		{
			return paths;
		}
		TomlLite.Doc doc = TomlLite.Parse(indexToml);
		for (int i = 0; i < doc.Files.Count; i++)
		{
			Dictionary<string, string> row = doc.Files[i];
			if (!row.TryGetValue("file", out string relative) || string.IsNullOrWhiteSpace(relative))
			{
				continue;
			}
			if (row.TryGetValue("preserve", out string preserve) && preserve.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				paths.Add(relative.Replace('\\', '/'));
			}
		}
		return paths;
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

	private static DiscoveredModFile ReadLines(string path, string relative, string format, bool preserved, List<SchemaSetting> hints, char separator)
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
			int split = trimmed.IndexOf(separator);
			if (split <= 0)
			{
				continue;
			}
			string key = trimmed.Substring(0, split).Trim().Trim('"');
			string value = trimmed.Substring(split + 1).Trim().Trim('"');
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
