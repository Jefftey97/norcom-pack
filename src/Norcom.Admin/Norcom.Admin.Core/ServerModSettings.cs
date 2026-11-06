using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public static class ServerModSettings
{
	private static readonly (string Token, string Id, string Title, string Group)[] Known = new(string, string, string, string)[28]
	{
		("simpleeco", "simpleeco", "Simple Eco", "Economy"),
		("schooleco", "simpleeco", "Simple Eco", "Economy"),
		("easy_npc", "easynpc", "Easy NPC", "NPCs"),
		("easynpc", "easynpc", "Easy NPC", "NPCs"),
		("minimotd", "minimotd", "MiniMOTD", "MOTD"),
		("mini-motd", "minimotd", "MiniMOTD", "MOTD"),
		("lithium", "lithium", "Lithium", "Performance"),
		("modernfix", "modernfix", "ModernFix", "Performance"),
		("ferritecore", "ferritecore", "FerriteCore", "Performance"),
		("c2me", "c2me", "C2ME", "Performance"),
		("krypton", "krypton", "Krypton", "Performance"),
		("waystones", "waystones", "Waystones", "Gameplay"),
		("hardcorerevival", "hardcorerevival", "Hardcore Revival", "Gameplay"),
		("graveyard", "graves", "Graves", "Gameplay"),
		("graves", "graves", "Graves", "Gameplay"),
		("sereneseason", "sereneseasons", "Serene Seasons", "World"),
		("toughasnails", "toughasnails", "Tough As Nails", "World"),
		("voicechat", "voicechat", "Voice Chat", "Gameplay"),
		("styledchat", "styledchat", "Styled Chat", "Gameplay"),
		("styledplayerlist", "styledplayerlist", "Styled Player List", "Gameplay"),
		("terralith", "terralith", "Terralith", "World"),
		("tectonic", "tectonic", "Tectonic", "World"),
		("incendium", "incendium", "Incendium", "World"),
		("structory", "structory", "Structory", "World"),
		("farmersdelight", "farmersdelight", "Farmer's Delight", "Gameplay"),
		("ftbquests", "ftbquests", "FTB Quests", "Quests"),
		("heracles", "heracles", "Heracles", "Quests"),
		("quests", "quests", "Quests", "Quests")
	};

	private static readonly (string Id, string Title, string Group)[] Expected = new(string, string, string)[3]
	{
		("simpleeco", "Simple Eco", "Economy"),
		("easynpc", "Easy NPC", "NPCs"),
		("minimotd", "MiniMOTD", "MOTD")
	};

	private static readonly Dictionary<string, (string Label, string Hint)> Labels = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
	{
		["startingMoney"] = ("Starting coins", "Coins a new player receives. NORCOM uses one currency."),
		["currencyName"] = ("Currency name", "Player-facing name, such as NORCOM Coins."),
		["currencySymbol"] = ("Currency symbol", "Short mark shown beside amounts."),
		["allowNegativeBalances"] = ("Allow negative balances", ""),
		["maxPaymentAmount"] = ("Largest payment", "Cap for /pay."),
		["enableLeaderboard"] = ("Balance leaderboard", ""),
		["enableSellSystem"] = ("Item selling", ""),
		["autosaveSeconds"] = ("Autosave interval", "Seconds between economy saves."),
		["motd-enabled"] = ("Server list MOTD", "MiniMOTD text in the multiplayer screen."),
		["icon-enabled"] = ("Server list icon", ""),
		["max-players"] = ("Listed max players", "The number shown next to the online count."),
		["max-players-enabled"] = ("Override listed max", ""),
		["disable-player-list-hover"] = ("Hide name hover", ""),
		["hide-player-count"] = ("Hide player count", ""),
		["allowBypassInvulnerability"] = ("Allow /kill on invulnerable NPCs", ""),
		["allowEscClose"] = ("Dialogs close with Esc", ""),
		["showCloseButton"] = ("Show the close button", ""),
		["displayAvatar"] = ("Show the NPC portrait", ""),
		["buttonConditionMode"] = ("Locked dialog buttons", "LOCK greys them out. HIDE removes them."),
		["blockPrivateAddresses"] = ("Block private skin URLs", ""),
		["DefaultSpawner=SpawnCount"] = ("Default spawner count", "Newly placed default spawners."),
		["DefaultSpawner=SpawnRange"] = ("Default spawner range", ""),
		["DefaultSpawner=RequiredPlayerRange"] = ("Default player range", "")
	};

	private static readonly string[] ClientTokens = new string[33]
	{
		"iris", "sodium", "shader", "complementary", "patrix", "distanthorizon", "continuity", "particlerain", "zoomify", "xaero",
		"appleskin", "roughlyenoughitems", "inventoryprofiles", "lambdynamic", "entity_texture", "entity_model", "notenoughanimation", "immediatelyfast", "moreculling", "entityculling",
		"reeses", "dynamic-fps", "dynamicfps", "firstperson", "cameraoverhaul", "visuality", "fallingleaves", "presencefootsteps", "sound-physics", "sound_physics",
		"dialog_client", "entitytexture", "entitymodel"
	};

	private static readonly string[] SecretTokens = new string[11]
	{
		"password", "token", "webhook", "secret", "apikey", "api_key", "api-key", "rcon", "private-key", "private_key",
		"discord"
	};

	public static IReadOnlyList<ServerModDocument> Scan(string serverDirectory)
	{
		List<ServerModDocument> list = new List<ServerModDocument>();
		if (!string.IsNullOrWhiteSpace(serverDirectory) && Directory.Exists(serverDirectory))
		{
			Dictionary<string, List<ServerSettingField>> dictionary = new Dictionary<string, List<ServerSettingField>>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, (string, string)> dictionary2 = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
			foreach (string item in Enumerate(serverDirectory))
			{
				string relative = Path.GetRelativePath(serverDirectory, item).Replace('\\', '/');
				if (SkipFile(relative))
				{
					continue;
				}
				(string, string, string)? tuple = Identify(relative);
				if (!tuple.HasValue)
				{
					continue;
				}
				List<ServerSettingField> list2 = ReadFields(item, relative).ToList();
				if (list2.Count != 0)
				{
					if (!dictionary.TryGetValue(tuple.Value.Item1, out var value))
					{
						value = new List<ServerSettingField>();
						dictionary[tuple.Value.Item1] = value;
						dictionary2[tuple.Value.Item1] = (tuple.Value.Item2, tuple.Value.Item3);
					}
					value.AddRange(list2);
				}
			}
			foreach (KeyValuePair<string, List<ServerSettingField>> item2 in dictionary)
			{
				(string, string) tuple2 = dictionary2[item2.Key];
				list.Add(new ServerModDocument(item2.Key, tuple2.Item1, tuple2.Item2, Found: true, item2.Value.Count + " settings", item2.Value));
			}
		}
		(string, string, string)[] expected = Expected;
		for (int i = 0; i < expected.Length; i++)
		{
			(string Id, string Title, string Group) expected2 = expected[i];
			if (!list.Any((ServerModDocument doc) => doc.ModId.Equals(expected2.Id, StringComparison.OrdinalIgnoreCase)))
			{
				list.Add(new ServerModDocument(expected2.Id, expected2.Title, expected2.Group, Found: false, "Not created yet. Start the server once, then refresh.", Array.Empty<ServerSettingField>()));
			}
		}
		return list.OrderBy((ServerModDocument doc) => GroupOrder(doc.Group)).ThenBy<ServerModDocument, string>((ServerModDocument doc) => doc.Title, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public static void Save(string serverDirectory, IReadOnlyList<ServerSettingEdit> edits)
	{
		if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
		{
			throw new DirectoryNotFoundException("Server folder was not found.");
		}
		foreach (IGrouping<string, ServerSettingEdit> item in from edit in edits
			group edit by edit.FileRelativeToServer.Replace('\\', '/'))
		{
			string key = item.Key;
			if (SkipFile(key) || !Identify(key).HasValue)
			{
				throw new InvalidOperationException("That file is not a server mod setting.");
			}
			string text = Resolve(serverDirectory, key);
			string contents = Apply(File.Exists(text) ? File.ReadAllText(text) : "", text, item);
			string text2 = Path.Combine(serverDirectory, "config", ".norcom-backup", key.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			Directory.CreateDirectory(Path.GetDirectoryName(text));
			if (File.Exists(text))
			{
				File.Copy(text, text2, overwrite: true);
			}
			File.WriteAllText(text, contents);
		}
	}

	public static bool IsHiddenFromAdmin(string relativePath)
	{
		string text = relativePath.Replace('\\', '/');
		if (!SkipFile(text) && !IsClientOnly(text))
		{
			return IsSecretPath(text);
		}
		return true;
	}

	public static bool IsClientOnly(string relativePath)
	{
		string path = relativePath.Replace('\\', '/').ToLowerInvariant();
		return ClientTokens.Any((string token) => path.Contains(token, StringComparison.Ordinal));
	}

	public static bool IsSecretKey(string key)
	{
		string text = key.Replace('\\', '/').ToLowerInvariant();
		return SecretTokens.Any((string token) => text.Contains(token, StringComparison.Ordinal));
	}

	private static IEnumerable<string> Enumerate(string serverDirectory)
	{
		string[] array = new string[3]
		{
			"config",
			Path.Combine(ServerPresence.LevelName(serverDirectory), "serverconfig"),
			"defaultconfigs"
		};
		foreach (string path in array)
		{
			string path2 = Path.Combine(serverDirectory, path);
			if (!Directory.Exists(path2))
			{
				continue;
			}
			foreach (string item in Directory.EnumerateFiles(path2, "*", SearchOption.AllDirectories))
			{
				bool flag;
				switch (Path.GetExtension(item))
				{
				case ".json":
				case ".json5":
				case ".properties":
				case ".cfg":
				case ".conf":
				case ".toml":
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					yield return item;
				}
			}
		}
	}

	private static bool SkipFile(string relative)
	{
		string text = relative.Replace('\\', '/').ToLowerInvariant();
		if (text.Contains(".norcom-backup/", StringComparison.Ordinal) || text.Contains("/preset/", StringComparison.Ordinal))
		{
			return true;
		}
		if (IsClientOnly(text) || IsSecretPath(text))
		{
			return true;
		}
		switch (Path.GetFileName(text))
		{
		case "economy.json":
		case "players.json":
		case "prices.json":
		case "messages.json":
		case "world-economy.json":
		case "price-overrides.json":
		case "price-tooltips.json":
			return true;
		default:
			return false;
		}
	}

	private static bool IsSecretPath(string path)
	{
		return SecretTokens.Any((string token) => path.Contains(token, StringComparison.Ordinal));
	}

	private static (string Id, string Title, string Group)? Identify(string relative)
	{
		string text = relative.Replace('\\', '/').ToLowerInvariant();
		(string, string, string, string)[] known = Known;
		for (int i = 0; i < known.Length; i++)
		{
			(string, string, string, string) tuple = known[i];
			if (text.Contains(tuple.Item1, StringComparison.Ordinal))
			{
				return (tuple.Item2, tuple.Item3, tuple.Item4);
			}
		}
		return null;
	}

	private static IEnumerable<ServerSettingField> ReadFields(string path, string relative)
	{
		string text = File.ReadAllText(path);
		IOrderedEnumerable<KeyValuePair<string, string>> source = ((path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json5", StringComparison.OrdinalIgnoreCase)) ? ReadJson(text) : ReadLines(text)).OrderByDescending<KeyValuePair<string, string>, bool>((KeyValuePair<string, string> pair) => Labels.ContainsKey(pair.Key)).ThenBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> pair) => pair.Key, StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> item3 in source.Take(36))
		{
			if (!IsSecretKey(item3.Key))
			{
				(string Label, string Hint) tuple = LabelFor(item3.Key);
				string item = tuple.Label;
				string item2 = tuple.Hint;
				string value = item3.Value;
				bool flag = ((value == "true" || value == "false") ? true : false);
				string kind = (flag ? "bool" : ((item3.Value.All((char ch) => char.IsDigit(ch) || ch == '-') && item3.Value.Length > 0) ? "int" : "text"));
				yield return new ServerSettingField(relative, item3.Key, item, item2, item3.Value, kind);
			}
		}
	}

	private static IEnumerable<KeyValuePair<string, string>> ReadJson(string text)
	{
		JsonNode jsonNode;
		try
		{
			jsonNode = JsonNode.Parse(text, null);
		}
		catch
		{
			yield break;
		}
		if (!(jsonNode is JsonObject obj2))
		{
			yield break;
		}
		foreach (KeyValuePair<string, string> item in Walk(obj2, ""))
		{
			yield return item;
		}
	}

	private static IEnumerable<KeyValuePair<string, string>> Walk(JsonObject obj, string prefix)
	{
		foreach (KeyValuePair<string, JsonNode> item in obj)
		{
			string text = (string.IsNullOrEmpty(prefix) ? item.Key : (prefix + "." + item.Key));
			if (IsSecretKey(text) || item.Value == null)
			{
				continue;
			}
			if (item.Value is JsonValue value)
			{
				yield return new KeyValuePair<string, string>(text, ValueText(value));
			}
			else
			{
				if (!(item.Value is JsonObject obj2) || prefix.Count((char ch) => ch == '.') >= 1)
				{
					continue;
				}
				foreach (KeyValuePair<string, string> item2 in Walk(obj2, text))
				{
					yield return item2;
				}
			}
		}
	}

	private static string ValueText(JsonValue value)
	{
		if (value.TryGetValue<bool>(out var value2))
		{
			if (!value2)
			{
				return "false";
			}
			return "true";
		}
		if (value.TryGetValue<long>(out var value3))
		{
			return value3.ToString();
		}
		if (value.TryGetValue<double>(out var value4) && Math.Abs(value4 - Math.Round(value4)) < 0.0001)
		{
			return ((long)Math.Round(value4)).ToString();
		}
		return value.ToString().Trim('"');
	}

	private static IEnumerable<KeyValuePair<string, string>> ReadLines(string text)
	{
		string[] array = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length == 0 || text2.StartsWith('#') || text2.StartsWith("//") || text2.StartsWith('['))
			{
				continue;
			}
			int num = text2.IndexOf('=');
			if (num > 0)
			{
				string text3 = text2.Substring(0, num).Trim();
				string text4 = text2;
				int num2 = num + 1;
				string text5 = text4.Substring(num2, text4.Length - num2).Trim().Trim('"');
				if (text5.Contains('=') && text3.Contains("Spawner", StringComparison.OrdinalIgnoreCase))
				{
					int num3 = text5.IndexOf('=');
					text3 = text3 + "=" + text5.Substring(0, num3).Trim();
					text4 = text5;
					num2 = num3 + 1;
					text5 = text4.Substring(num2, text4.Length - num2).Trim();
				}
				if (!IsSecretKey(text3) && (!text3.StartsWith("DefaultSpawner=", StringComparison.Ordinal) || text3.EndsWith("SpawnCount", StringComparison.Ordinal) || text3.EndsWith("SpawnRange", StringComparison.Ordinal) || text3.EndsWith("RequiredPlayerRange", StringComparison.Ordinal)))
				{
					yield return new KeyValuePair<string, string>(text3, text5);
				}
			}
		}
	}

	private static string Apply(string original, string path, IEnumerable<ServerSettingEdit> edits)
	{
		List<ServerSettingEdit> edits2 = edits.Where((ServerSettingEdit edit) => !IsSecretKey(edit.Key)).ToList();
		if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			return ApplyJson(original, edits2);
		}
		return ApplyLines(original, edits2);
	}

	private static string ApplyJson(string original, List<ServerSettingEdit> edits)
	{
		JsonObject jsonObject = (JsonNode.Parse(string.IsNullOrWhiteSpace(original) ? "{}" : original, null) as JsonObject) ?? throw new InvalidDataException("That config is not a JSON object.");
		foreach (ServerSettingEdit edit in edits)
		{
			string[] array = edit.Key.Split('.');
			JsonObject jsonObject2 = jsonObject;
			for (int i = 0; i < array.Length - 1; i++)
			{
				JsonObject jsonObject3 = jsonObject2[array[i]] as JsonObject;
				if (jsonObject3 == null)
				{
					jsonObject3 = new JsonObject((JsonNodeOptions?)null);
					jsonObject2[array[i]] = jsonObject3;
				}
				jsonObject2 = jsonObject3;
			}
			jsonObject2[array[^1]] = MakeJson(jsonObject2[array[^1]], edit.Value);
		}
		return jsonObject.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}) + "\n";
	}

	private static JsonNode MakeJson(JsonNode? previous, string value)
	{
		if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
		{
			return JsonValue.Create(value: true, (JsonNodeOptions?)null);
		}
		if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
		{
			return JsonValue.Create(value: false, (JsonNodeOptions?)null);
		}
		if (previous is JsonValue jsonValue && jsonValue.TryGetValue<long>(out var _) && long.TryParse(value, out var result))
		{
			return JsonValue.Create(result, (JsonNodeOptions?)null);
		}
		if (long.TryParse(value, out var result2) && previous == null)
		{
			return JsonValue.Create(result2, (JsonNodeOptions?)null);
		}
		return JsonValue.Create(value, (JsonNodeOptions?)null);
	}

	private static string ApplyLines(string original, List<ServerSettingEdit> edits)
	{
		string text = (original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
		List<string> list = original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
		if (list.Count > 0)
		{
			if (list[list.Count - 1].Length == 0)
			{
				list.RemoveAt(list.Count - 1);
			}
		}
		Dictionary<string, string> dictionary = edits.ToDictionary<ServerSettingEdit, string, string>((ServerSettingEdit edit) => edit.Key, (ServerSettingEdit edit) => edit.Value, StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < list.Count; i++)
		{
			string text2 = list[i].Trim();
			int num = text2.IndexOf('=');
			if (num > 0)
			{
				string text3 = text2.Substring(0, num).Trim();
				string text4 = text2;
				int num2 = num + 1;
				string text5 = text4.Substring(num2, text4.Length - num2).Trim();
				if (text5.Contains('=') && text3.Contains("Spawner", StringComparison.OrdinalIgnoreCase))
				{
					text3 = text3 + "=" + text5[..text5.IndexOf('=')].Trim();
				}
				if (dictionary.TryGetValue(text3, out var value))
				{
					text4 = list[i];
					num2 = text2.Length;
					string text6 = text4.Substring(0, text4.Length - num2);
					list[i] = text6 + text3 + "=" + value;
					dictionary.Remove(text3);
				}
			}
		}
		foreach (KeyValuePair<string, string> item in dictionary)
		{
			list.Add(item.Key + "=" + item.Value);
		}
		return string.Join(text, list) + text;
	}

	private static string Resolve(string serverDirectory, string relative)
	{
		string fullPath = Path.GetFullPath(serverDirectory);
		string fullPath2 = Path.GetFullPath(Path.Combine(fullPath, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath2.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("That setting path leaves the server folder.");
		}
		string text = fullPath2;
		int num = fullPath.Length + 1;
		string text2 = text.Substring(num, text.Length - num).Replace('\\', '/');
		if (!text2.StartsWith("config/", StringComparison.OrdinalIgnoreCase) && !text2.StartsWith("world/serverconfig/", StringComparison.OrdinalIgnoreCase) && !text2.StartsWith("defaultconfigs/", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Server mod settings stay inside config.");
		}
		return fullPath2;
	}

	private static (string Label, string Hint) LabelFor(string key)
	{
		if (Labels.TryGetValue(key, out (string, string) value))
		{
			return value;
		}
		string key2 = (key.Contains('=', StringComparison.Ordinal) ? key : Last(key));
		if (!Labels.TryGetValue(key2, out value))
		{
			return (Label: Humanize(key2), Hint: "");
		}
		return value;
	}

	private static string Last(string key)
	{
		int num = key.LastIndexOf('.');
		if (num >= 0)
		{
			int num2 = num + 1;
			return key.Substring(num2, key.Length - num2);
		}
		return key;
	}

	private static string Humanize(string key)
	{
		string input = Last(key).Replace('_', ' ').Replace('-', ' ');
		input = System.Text.RegularExpressions.Regex.Replace(input, "([A-Z])", " $1");
		input = input.Trim();
		if (input.Length != 0)
		{
			ReadOnlySpan<char> readOnlySpan = new ReadOnlySpan<char>(char.ToUpperInvariant(input[0]));
			string text = input;
			return string.Concat(readOnlySpan, text.Substring(1, text.Length - 1));
		}
		return key;
	}

	private static int GroupOrder(string group)
	{
		return group switch
		{
			"Economy" => 0, 
			"NPCs" => 1, 
			"MOTD" => 2, 
			"Performance" => 3, 
			"World" => 4, 
			"Quests" => 5, 
			"Gameplay" => 6, 
			_ => 7, 
		};
	}
}
