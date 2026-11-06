using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norcom.Launcher.Core.Models;

public sealed class LauncherSettings
{
	private static readonly string[] KnownKeys = new string[28]
	{
		"RamMb", "ramMb", "GraphicsTier", "graphicsTier", "GraphicsAuto", "graphicsAuto", "LauncherSounds", "launcherSounds", "CloseToTray", "closeToTray",
		"ReduceMotion", "reduceMotion", "AnimationsPausedWhilePlaying", "animationsPausedWhilePlaying", "PlayerName", "playerName", "UseLocalName", "useLocalName", "DiscordInviteUrl", "discordInviteUrl",
		"CrashWebhookUrl", "crashWebhookUrl", "WindowW", "windowW", "WindowH", "windowH", "LastNewsPackVersion", "lastNewsPackVersion"
	};

	public int RamMb { get; set; } = 8192;


	public GraphicsTier GraphicsTier { get; set; } = GraphicsTier.High;


	public bool GraphicsAuto { get; set; }

	public bool LauncherSounds { get; set; }

	public bool CloseToTray { get; set; }


	public bool ReduceMotion { get; set; }

	public bool AnimationsPausedWhilePlaying { get; set; } = true;


	public string PlayerName { get; set; } = "";


	public bool UseLocalName { get; set; }

	public string DiscordInviteUrl { get; set; } = "";


	public string CrashWebhookUrl { get; set; } = "";


	public int WindowW { get; set; } = 1180;


	public int WindowH { get; set; } = 760;


	public string? LastNewsPackVersion { get; set; }

	public JsonObject Extra { get; set; } = new JsonObject();


	public static LauncherSettings Load(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return new LauncherSettings();
			}
			if (!(JsonNode.Parse(File.ReadAllText(path)) is JsonObject jsonObject))
			{
				return new LauncherSettings();
			}
			LauncherSettings launcherSettings = new LauncherSettings
			{
				RamMb = Math.Clamp(ReadInt(jsonObject, "RamMb", "ramMb").GetValueOrDefault(8192), 2048, 16384),
				GraphicsTier = ReadTier(jsonObject).GetValueOrDefault(GraphicsTier.High),
				GraphicsAuto = ReadBool(jsonObject, "GraphicsAuto", "graphicsAuto").GetValueOrDefault(),
				LauncherSounds = ReadBool(jsonObject, "LauncherSounds", "launcherSounds").GetValueOrDefault(),
				CloseToTray = false,
				ReduceMotion = ReadBool(jsonObject, "ReduceMotion", "reduceMotion").GetValueOrDefault(),
				AnimationsPausedWhilePlaying = ReadBool(jsonObject, "AnimationsPausedWhilePlaying", "animationsPausedWhilePlaying").GetValueOrDefault(true),
				PlayerName = (ReadString(jsonObject, "PlayerName", "playerName") ?? ""),
				UseLocalName = ReadBool(jsonObject, "UseLocalName", "useLocalName").GetValueOrDefault(),
				DiscordInviteUrl = (ReadString(jsonObject, "DiscordInviteUrl", "discordInviteUrl") ?? ""),
				CrashWebhookUrl = (ReadString(jsonObject, "CrashWebhookUrl", "crashWebhookUrl") ?? ""),
				WindowW = ReadInt(jsonObject, "WindowW", "windowW").GetValueOrDefault(1180),
				WindowH = ReadInt(jsonObject, "WindowH", "windowH").GetValueOrDefault(760),
				LastNewsPackVersion = ReadString(jsonObject, "LastNewsPackVersion", "lastNewsPackVersion")
			};
			launcherSettings.PlayerName = launcherSettings.PlayerName.Trim();
			JsonObject jsonObject2 = new JsonObject();
			foreach (KeyValuePair<string, JsonNode> item in jsonObject)
			{
				if (!KnownKeys.Contains<string>(item.Key, StringComparer.OrdinalIgnoreCase) && item.Value != null)
				{
					jsonObject2[item.Key] = item.Value.DeepClone();
				}
			}
			launcherSettings.Extra = jsonObject2;
			return launcherSettings;
		}
		catch
		{
			return new LauncherSettings();
		}
	}

	public void Save(string path)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		RamMb = Math.Clamp(RamMb, 2048, 16384);
		JsonObject jsonObject = new JsonObject();
		foreach (KeyValuePair<string, JsonNode> item in Extra)
		{
			if (!KnownKeys.Contains<string>(item.Key, StringComparer.OrdinalIgnoreCase) && item.Value != null)
			{
				jsonObject[item.Key] = item.Value.DeepClone();
			}
		}
		jsonObject["RamMb"] = RamMb;
		jsonObject["GraphicsTier"] = GraphicsTier.ToString();
		jsonObject["GraphicsAuto"] = GraphicsAuto;
		jsonObject["LauncherSounds"] = LauncherSounds;
		jsonObject["CloseToTray"] = false;
		jsonObject["ReduceMotion"] = ReduceMotion;
		jsonObject["AnimationsPausedWhilePlaying"] = AnimationsPausedWhilePlaying;
		jsonObject["PlayerName"] = PlayerName;
		jsonObject["UseLocalName"] = UseLocalName;
		jsonObject["DiscordInviteUrl"] = DiscordInviteUrl;
		jsonObject["CrashWebhookUrl"] = CrashWebhookUrl;
		jsonObject["WindowW"] = WindowW;
		jsonObject["WindowH"] = WindowH;
		if (!string.IsNullOrWhiteSpace(LastNewsPackVersion))
		{
			jsonObject["LastNewsPackVersion"] = LastNewsPackVersion;
		}
		File.WriteAllText(path, jsonObject.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static int? ReadInt(JsonObject node, string pascal, string camel)
	{
		JsonNode jsonNode = node[pascal] ?? node[camel];
		if (jsonNode == null)
		{
			return null;
		}
		if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value))
		{
			return value;
		}
		if (!int.TryParse(jsonNode.ToString(), out var result))
		{
			return null;
		}
		return result;
	}

	private static bool? ReadBool(JsonObject node, string pascal, string camel)
	{
		JsonNode jsonNode = node[pascal] ?? node[camel];
		if (jsonNode == null)
		{
			return null;
		}
		if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var value))
		{
			return value;
		}
		if (!bool.TryParse(jsonNode.ToString(), out var result))
		{
			return null;
		}
		return result;
	}

	private static string? ReadString(JsonObject node, string pascal, string camel)
	{
		JsonNode jsonNode = node[pascal] ?? node[camel];
		if (jsonNode == null)
		{
			return null;
		}
		if (!(jsonNode is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value))
		{
			return jsonNode.ToString();
		}
		return value;
	}

	private static GraphicsTier? ReadTier(JsonObject node)
	{
		JsonNode jsonNode = node["GraphicsTier"] ?? node["graphicsTier"];
		if (jsonNode == null)
		{
			return null;
		}
		if (jsonNode is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var value) && Enum.IsDefined(typeof(GraphicsTier), value))
		{
			return (GraphicsTier)value;
		}
		if (!Enum.TryParse<GraphicsTier>(jsonNode.ToString(), ignoreCase: true, out var result))
		{
			return null;
		}
		return result;
	}
}
