using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class GraphicsService
{
	private static readonly string[] CacheDirs = new string[8] { "shaderpacks/shadercache", "shader_cache", "cache/iris", "iris-cache", ".iris-cache", "shaderpacks/.iris", "config/iris-shadercache", "config/iris-reserved/cache" };

	public GraphicsPreset Apply(string gameDir, GraphicsTier tier)
	{
		string folder = GraphicsPack.Folder(tier);
		GraphicsPreset graphicsPreset = GraphicsPresets.For(tier);
		Directory.CreateDirectory(gameDir);
		Directory.CreateDirectory(Path.Combine(gameDir, "config"));
		Directory.CreateDirectory(Path.Combine(gameDir, "shaderpacks"));
		Dictionary<string, string> updates = GraphicsPack.ReadJsonMap(folder + "/options-patches.json");
		updates["resourcePacks"] = GraphicsPack.ReadText(folder + "/resourcePacks.txt").Trim();
		string path = Path.Combine(gameDir, "options.txt");
		string existing = (File.Exists(path) ? File.ReadAllText(path) : null);
		File.WriteAllText(path, OptionsPatch.Apply(existing, updates));
		string path2 = Path.Combine(gameDir, "config", "iris.properties");
		string existing2 = (File.Exists(path2) ? File.ReadAllText(path2) : null);
		File.WriteAllText(path2, OptionsPatch.ApplyProperties(existing2, GraphicsPack.ReadProperties(folder + "/iris.properties")));
		string shader = GraphicsPack.ReadText(folder + "/ComplementaryUnbound_r5.9.3.txt");
		string shaderZip = GraphicsPack.ReadText(folder + "/ComplementaryUnbound_r5.9.3.zip.txt");
		File.WriteAllText(Path.Combine(gameDir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), shader);
		File.WriteAllText(Path.Combine(gameDir, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip.txt"), shaderZip);
		WriteDistantHorizons(Path.Combine(gameDir, "config", "DistantHorizons.toml"), GraphicsPack.ReadJsonMap(folder + "/dh-patches.json"));
		MergeSodium(Path.Combine(gameDir, "config", "sodium-options.json"), GraphicsPack.ReadJsonElements(folder + "/sodium-patches.json"));
		string tierNote = "tier=" + tier.ToString() + "\nappliedUtc=" + DateTimeOffset.UtcNow.ToString("o") + "\n";
		File.WriteAllText(Path.Combine(gameDir, "config", "norcom-tier.txt"), tierNote);
		File.WriteAllText(Path.Combine(gameDir, "norcom-graphics-tier.txt"), tierNote);
		LauncherLog.Write("Graphics applied " + GraphicsPresets.TierLabel(tier));
		return graphicsPreset;
	}

	public string BackupAndReset(string gameDir, GraphicsTier tier)
	{
		string text = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string text2 = Path.Combine(gameDir, "norcom-backups", "visuals-" + text);
		Directory.CreateDirectory(text2);
		string[] array = new string[6] { "options.txt", "config/iris.properties", "config/sodium-options.json", "config/DistantHorizons.toml", "shaderpacks/ComplementaryUnbound_r5.9.3.txt", "shaderpacks/ComplementaryUnbound_r5.9.3.zip.txt" };
		foreach (string text3 in array)
		{
			string text4 = Path.Combine(gameDir, text3.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(text4))
			{
				string text5 = Path.Combine(text2, text3.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(text5));
				File.Copy(text4, text5, overwrite: true);
			}
		}
		Apply(gameDir, tier);
		return text2;
	}

	public IReadOnlyList<string> ClearShaderCache(string gameDir)
	{
		List<string> list = new List<string>();
		string[] cacheDirs = CacheDirs;
		foreach (string text in cacheDirs)
		{
			string path = Path.Combine(gameDir, text.Replace('/', Path.DirectorySeparatorChar));
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
					list.Add(text);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("Shader cache clear failed " + text + ": " + ex.Message);
				}
			}
		}
		return list;
	}

	private static void WriteDistantHorizons(string path, IReadOnlyDictionary<string, string> patches)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = File.Exists(path) ? File.ReadAllText(path) : "[client]\n[client.advanced]\n[client.advanced.graphics]\n[client.advanced.graphics.quality]\n";
		foreach (KeyValuePair<string, string> patch in patches)
		{
			text = ReplaceToml(text, patch.Key, TomlValue(patch.Value));
		}
		File.WriteAllText(path, text);
	}

	private static string TomlValue(string value)
	{
		if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
		{
			return value.ToLowerInvariant();
		}
		if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
		{
			return value;
		}
		return "\"" + value + "\"";
	}

	private static void MergeSodium(string path, IReadOnlyDictionary<string, JsonElement> patches)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		JsonObject root = new JsonObject();
		if (File.Exists(path))
		{
			try
			{
				if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
				{
					root = existing;
				}
			}
			catch (JsonException)
			{
			}
		}
		foreach (KeyValuePair<string, JsonElement> patch in patches)
		{
			string[] parts = patch.Key.Split('.');
			JsonObject cursor = root;
			for (int i = 0; i < parts.Length - 1; i++)
			{
				if (cursor[parts[i]] is not JsonObject child)
				{
					child = new JsonObject();
					cursor[parts[i]] = child;
				}
				cursor = child;
			}
			cursor[parts[parts.Length - 1]] = JsonNode.Parse(patch.Value.GetRawText());
		}
		File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}

	private static string ReplaceToml(string text, string key, string value)
	{
		string[] array = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].TrimStart();
			if (text2.StartsWith(key, StringComparison.Ordinal))
			{
				string text3 = text2;
				int length = key.Length;
				if (text3.Substring(length, text3.Length - length).TrimStart().StartsWith('='))
				{
					text3 = array[i];
					length = text2.Length;
					string text4 = text3.Substring(0, text3.Length - length);
					array[i] = text4 + key + " = " + value;
					flag = true;
				}
			}
		}
		string text5 = string.Join('\n', array);
		if (!flag)
		{
			text5 = text5.TrimEnd() + "\n" + key + " = " + value + "\n";
		}
		return text5;
	}

}
