using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class TexturePackChoice
{
	public const string Patrix = "Patrix";

	public const string Alacrity = "Alacrity";

	public const string LabPbrHint = "Emerald and ore glow needs Shader Settings > Resource Pack Support = labPBR.";

	public static readonly string[] PatrixFiles = new string[3] { "Patrix_26.3_32x_basic.zip", "Patrix_26.3_32x_addon.zip", "Patrix_26.3_models.zip" };

	public static string Normalize(string? choice)
	{
		return string.Equals(choice, Patrix, StringComparison.OrdinalIgnoreCase) ? Patrix : Alacrity;
	}

	public static string Detect(string? resourcePacks)
	{
		List<string> items = GraphicsPack.ParsePacks(resourcePacks ?? "");
		int patrix = -1;
		int alacrity = -1;
		for (int i = 0; i < items.Count; i++)
		{
			if (IsPatrix(items[i]))
			{
				patrix = i;
			}
			if (items[i].Equals("file/Alacrity.zip", StringComparison.OrdinalIgnoreCase))
			{
				alacrity = i;
			}
		}
		if (patrix < 0 && alacrity < 0)
		{
			return Alacrity;
		}
		if (patrix >= 0 && alacrity < 0)
		{
			return Patrix;
		}
		if (alacrity >= 0 && patrix < 0)
		{
			return Alacrity;
		}
		return patrix > alacrity ? Patrix : Alacrity;
	}

	public static string ReadEnabled(string gameDir)
	{
		return ReadKey(ReadOptions(gameDir), "resourcePacks");
	}

	public static bool PrepareForLaunch(string gameDir, string? savedChoice, bool saved, bool pendingApply)
	{
		if (!pendingApply)
		{
			return false;
		}
		string choice = saved ? Normalize(savedChoice) : Alacrity;
		return ApplyPicker(gameDir, choice);
	}

	public static bool ApplyPicker(string gameDir, string choice)
	{
		choice = Normalize(choice);
		BringFilesToTop(gameDir, choice);
		return ApplyListIfDifferent(gameDir, choice);
	}

	public static bool ApplyListIfDifferent(string gameDir, string choice)
	{
		choice = Normalize(choice);
		string existing = ReadOptions(gameDir);
		string packs = ReadKey(existing, "resourcePacks");
		string blocked = ReadKey(existing, "incompatibleResourcePacks");
		string nextPacks = BuildList(packs, choice);
		string nextBlocked = DropEnabled(blocked, EnabledIds(choice));
		if (GraphicsPack.SamePackList(packs, nextPacks) && GraphicsPack.SamePackList(blocked, nextBlocked))
		{
			return false;
		}
		string updated = ReplaceKeys(existing, nextPacks, nextBlocked);
		Directory.CreateDirectory(gameDir);
		File.WriteAllText(Path.Combine(gameDir, "options.txt"), updated);
		return true;
	}

	public static void BringFilesToTop(string gameDir, string choice)
	{
		string top = Path.Combine(gameDir, "resourcepacks");
		string held = Path.Combine(top, "norcom-held");
		string[] names = Normalize(choice) == Patrix ? PatrixFiles : new string[1] { "Alacrity.zip" };
		for (int i = 0; i < names.Length; i++)
		{
			string from = Path.Combine(held, names[i]);
			string to = Path.Combine(top, names[i]);
			if (!File.Exists(from) || File.Exists(to))
			{
				continue;
			}
			Directory.CreateDirectory(top);
			File.Move(from, to);
		}
	}

	public static string Hint(string gameDir, string choice)
	{
		if (!string.Equals(Normalize(choice), Patrix, StringComparison.Ordinal))
		{
			return "";
		}
		string shader = ReadKey(ReadText(Path.Combine(gameDir, "config", "iris.properties")), "shaderPack");
		if (shader.Contains("Complementary", StringComparison.OrdinalIgnoreCase))
		{
			return LabPbrHint;
		}
		string folder = Path.Combine(gameDir, "shaderpacks");
		if (Directory.Exists(folder))
		{
			foreach (string path in Directory.EnumerateFiles(folder, "Complementary*"))
			{
				if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
				{
					return LabPbrHint;
				}
			}
		}
		return "";
	}

	public static string BuildList(string packs, string choice)
	{
		List<string> items = GraphicsPack.ParsePacks(packs);
		for (int i = items.Count - 1; i >= 0; i--)
		{
			if (IsPatrix(items[i]) || items[i].Equals("file/Alacrity.zip", StringComparison.OrdinalIgnoreCase))
			{
				items.RemoveAt(i);
			}
		}
		int at = Index(items, "continuity:default");
		if (at < 0)
		{
			at = Index(items, "vanilla");
		}
		int insert = at + 1;
		string[] add = Normalize(choice) == Patrix
			? new string[3] { "file/" + PatrixFiles[0], "file/" + PatrixFiles[1], "file/" + PatrixFiles[2] }
			: new string[1] { "file/Alacrity.zip" };
		for (int i = 0; i < add.Length; i++)
		{
			items.Insert(insert, add[i]);
			insert++;
		}
		return GraphicsPack.FormatPacks(items);
	}

	private static string[] EnabledIds(string choice)
	{
		if (Normalize(choice) == Patrix)
		{
			return new string[3] { "file/" + PatrixFiles[0], "file/" + PatrixFiles[1], "file/" + PatrixFiles[2] };
		}
		return new string[1] { "file/Alacrity.zip" };
	}

	private static string DropEnabled(string blocked, string[] enabled)
	{
		List<string> items = GraphicsPack.ParsePacks(blocked);
		for (int i = items.Count - 1; i >= 0; i--)
		{
			for (int e = 0; e < enabled.Length; e++)
			{
				if (items[i].Equals(enabled[e], StringComparison.OrdinalIgnoreCase))
				{
					items.RemoveAt(i);
					break;
				}
			}
		}
		return GraphicsPack.FormatPacks(items);
	}

	private static string ReplaceKeys(string existing, string packs, string blocked)
	{
		string newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string[] lines = existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		bool sawPacks = false;
		bool sawBlocked = false;
		StringBuilder builder = new StringBuilder();
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			if (i == lines.Length - 1 && line.Length == 0)
			{
				continue;
			}
			if (IsKey(line, "resourcePacks"))
			{
				line = "resourcePacks:" + packs;
				sawPacks = true;
			}
			else if (IsKey(line, "incompatibleResourcePacks"))
			{
				line = "incompatibleResourcePacks:" + blocked;
				sawBlocked = true;
			}
			builder.Append(line);
			builder.Append(newline);
		}
		if (!sawPacks)
		{
			builder.Append("resourcePacks:" + packs);
			builder.Append(newline);
		}
		if (!sawBlocked && !string.Equals(blocked, "[]", StringComparison.Ordinal))
		{
			builder.Append("incompatibleResourcePacks:" + blocked);
			builder.Append(newline);
		}
		return builder.ToString();
	}

	private static bool IsKey(string line, string key)
	{
		return IsKey(line, key, ':');
	}

	private static string ReadOptions(string gameDir)
	{
		return ReadText(Path.Combine(gameDir, "options.txt"));
	}

	private static string ReadText(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : "";
		}
		catch (IOException)
		{
			return "";
		}
	}

	private static string ReadKey(string text, string key)
	{
		string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			char separator = line.Contains(':', StringComparison.Ordinal) ? ':' : '=';
			if (!IsKey(line, key, separator))
			{
				continue;
			}
			return line.Substring(line.IndexOf(separator) + 1).Trim();
		}
		return "";
	}

	private static bool IsKey(string line, string key, char separator)
	{
		if (line.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		int split = line.IndexOf(separator);
		return split > 0 && line.Substring(0, split).Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPatrix(string item)
	{
		return item.Contains("Patrix_26.3", StringComparison.OrdinalIgnoreCase);
	}

	private static int Index(List<string> items, string name)
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
}
