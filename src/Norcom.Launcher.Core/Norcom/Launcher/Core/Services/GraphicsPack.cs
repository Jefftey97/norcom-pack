using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public static class GraphicsPack
{
	public const string AlacrityPacks = "[\"vanilla\",\"continuity:default\",\"file/Alacrity.zip\",\"file/norcom-alacrity-candle-glass-fix.zip\",\"file/norcom_marble.zip\"]";

	public const string OverlayFile = "norcom-alacrity-candle-glass-fix.zip";

	public const string OverlaySha256 = "b820f0a09bd5341ee262f23dd5963bc64d4656c1b1a2e1562a9f825f98009b2a";

	public const string MarbleFile = "norcom_marble.zip";

	public const string MarbleSha256 = "642da745a516161b4e69d366c29bcaa9270bd42956c114706e1f19d6b901f039";

	public const int MarbleBytes = 24710796;

	public const string SupersededMarbleSha256 = "80770a4a8735188b8b9ac5e2f0d1ca8247bd76b17b86edde659261b7b1c21db5";

	public const string MarbleUrl = "https://github.com/Jefftey97/norcom-pack/releases/download/launcher-2.0.36/norcom_marble.zip";

	public const string NeverEnable = "continuity:glass_pane_culling_fix";

	public static readonly string[] NeverStack = new string[4] { "Patrix", "Faithful", "FreshAnimations", "FreshMoves" };

	public static string HealPackList(string packs)
	{
		List<string> items = ParsePacks(packs);
		bool alacrity = false;
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Equals("file/Alacrity.zip", StringComparison.OrdinalIgnoreCase))
			{
				alacrity = true;
				break;
			}
		}
		RemoveNamed(items, NeverEnable);
		if (alacrity)
		{
			for (int i = items.Count - 1; i >= 0; i--)
			{
				if (Stacks(items[i]))
				{
					items.RemoveAt(i);
				}
			}
			RemoveEquals(items, "vanilla");
			RemoveEquals(items, "continuity:default");
			RemoveEquals(items, "file/Alacrity.zip");
			RemoveEquals(items, "file/" + OverlayFile);
			RemoveEquals(items, "file/" + MarbleFile);
			items.Insert(0, "file/" + MarbleFile);
			items.Insert(0, "file/" + OverlayFile);
			items.Insert(0, "file/Alacrity.zip");
			items.Insert(0, "continuity:default");
			items.Insert(0, "vanilla");
		}
		else
		{
			if (!ContainsEquals(items, "vanilla"))
			{
				items.Insert(0, "vanilla");
			}
			if (!ContainsEquals(items, "continuity:default"))
			{
				int at = IndexEquals(items, "vanilla");
				items.Insert(at + 1, "continuity:default");
			}
			if (!ContainsEquals(items, "file/" + MarbleFile))
			{
				items.Add("file/" + MarbleFile);
			}
		}
		return FormatPacks(items);
	}

	public static bool SamePackList(string left, string right)
	{
		List<string> a = ParsePacks(left);
		List<string> b = ParsePacks(right);
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	public static List<string> ParsePacks(string packs)
	{
		List<string> items = new List<string>();
		if (string.IsNullOrWhiteSpace(packs))
		{
			return items;
		}
		string text = packs.Trim();
		if (text.StartsWith("[", StringComparison.Ordinal))
		{
			text = text.Substring(1);
		}
		if (text.EndsWith("]", StringComparison.Ordinal))
		{
			text = text.Substring(0, text.Length - 1);
		}
		string[] parts = text.Split(',');
		for (int i = 0; i < parts.Length; i++)
		{
			string item = parts[i].Trim().Trim('"').Trim();
			if (item.Length > 0)
			{
				items.Add(item);
			}
		}
		return items;
	}

	public static string FormatPacks(IReadOnlyList<string> items)
	{
		StringBuilder builder = new StringBuilder();
		builder.Append('[');
		for (int i = 0; i < items.Count; i++)
		{
			if (i > 0)
			{
				builder.Append(',');
			}
			builder.Append('"');
			builder.Append(items[i]);
			builder.Append('"');
		}
		builder.Append(']');
		return builder.ToString();
	}

	private static bool Stacks(string item)
	{
		for (int i = 0; i < NeverStack.Length; i++)
		{
			if (item.Contains(NeverStack[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void RemoveNamed(List<string> items, string name)
	{
		for (int i = items.Count - 1; i >= 0; i--)
		{
			if (items[i].Contains(name, StringComparison.OrdinalIgnoreCase))
			{
				items.RemoveAt(i);
			}
		}
	}

	private static void RemoveEquals(List<string> items, string name)
	{
		for (int i = items.Count - 1; i >= 0; i--)
		{
			if (items[i].Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				items.RemoveAt(i);
			}
		}
	}

	private static bool ContainsEquals(List<string> items, string name)
	{
		return IndexEquals(items, name) >= 0;
	}

	private static int IndexEquals(List<string> items, string name)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	public static string Folder(GraphicsTier tier)
	{
		return tier switch
		{
			GraphicsTier.Low => "low",
			GraphicsTier.Medium => "medium",
			GraphicsTier.MediumHigh => "medium_high",
			GraphicsTier.Ultra => "ultra",
			_ => "high",
		};
	}

	public static string ReadText(string relativePath)
	{
		string name = ResourceName(relativePath);
		using Stream stream = Open(name);
		using StreamReader reader = new StreamReader(stream);
		return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
	}

	public static Dictionary<string, string> ReadJsonMap(string relativePath)
	{
		using JsonDocument document = JsonDocument.Parse(ReadText(relativePath));
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty property in document.RootElement.EnumerateObject())
		{
			map[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
		}
		return map;
	}

	public static Dictionary<string, JsonElement> ReadJsonElements(string relativePath)
	{
		JsonDocument document = JsonDocument.Parse(ReadText(relativePath));
		Dictionary<string, JsonElement> map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (JsonProperty property in document.RootElement.EnumerateObject())
		{
			map[property.Name] = property.Value.Clone();
		}
		return map;
	}

	public static Dictionary<string, string> ReadProperties(string relativePath)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in ReadText(relativePath).Split('\n'))
		{
			string text = line.Trim();
			if (text.Length == 0 || text.StartsWith('#'))
			{
				continue;
			}
			int split = text.IndexOf('=');
			if (split > 0)
			{
				map[text.Substring(0, split).Trim()] = text.Substring(split + 1).Trim();
			}
		}
		return map;
	}

	private static Stream Open(string name)
	{
		Stream? stream = typeof(GraphicsPack).Assembly.GetManifestResourceStream(name);
		if (stream != null)
		{
			return stream;
		}
		foreach (string resource in typeof(GraphicsPack).Assembly.GetManifestResourceNames())
		{
			if (resource.EndsWith(name, StringComparison.OrdinalIgnoreCase) || resource.Replace('\\', '.').EndsWith(name, StringComparison.OrdinalIgnoreCase))
			{
				return typeof(GraphicsPack).Assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException(resource);
			}
		}
		throw new FileNotFoundException("Graphics pack resource " + name + " was not in the launcher.");
	}

	private static string ResourceName(string relativePath)
	{
		string normalized = relativePath.Replace('\\', '/').TrimStart('/');
		return "Norcom.Launcher.Core.GraphicsPack." + normalized.Replace('/', '.');
	}
}
