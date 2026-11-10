using System;
using System.Collections.Generic;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class TomlValuePatch
{
	public static bool TryApply(string text, IReadOnlyDictionary<string, string> updates, out string updated, out string error)
	{
		updated = text ?? "";
		error = "";
		if (updates == null || updates.Count == 0)
		{
			return true;
		}
		string newline = (text ?? "").Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string body = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
		bool trailing = body.EndsWith("\n", StringComparison.Ordinal);
		if (trailing)
		{
			body = body.Substring(0, body.Length - 1);
		}
		string[] lines = body.Length == 0 ? Array.Empty<string>() : body.Split('\n');
		string section = "";
		bool arrayTable = false;
		Dictionary<string, int> sectionTail = new Dictionary<string, int>(StringComparer.Ordinal);
		sectionTail[""] = -1;
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < lines.Length; i++)
		{
			if (!TryReadLine(lines[i], section, arrayTable, out LineKind kind, out string fullKey, out bool inline, out string nextSection, out bool nextArray, out int valueStart, out int valueLength, out error))
			{
				updated = text ?? "";
				return false;
			}
			if (kind == LineKind.Header)
			{
				section = nextSection;
				arrayTable = nextArray;
				sectionTail[section] = i;
				continue;
			}
			sectionTail[section] = i;
			if (kind != LineKind.Assignment || !updates.TryGetValue(fullKey, out string replacement))
			{
				continue;
			}
			if (inline || arrayTable)
			{
				error = "TOML inline tables and arrays are read-only: " + fullKey;
				updated = text ?? "";
				return false;
			}
			lines[i] = ReplaceValue(lines[i], valueStart, valueLength, replacement);
			seen.Add(fullKey);
		}
		List<string> output = new List<string>(lines);
		foreach (KeyValuePair<string, string> update in updates)
		{
			if (seen.Contains(update.Key))
			{
				continue;
			}
			SplitKey(update.Key, out string table, out string leaf);
			if (leaf.Length == 0)
			{
				error = "TOML key is not a value: " + update.Key;
				updated = text ?? "";
				return false;
			}
			string line = leaf + " = " + FormatNewValue(update.Value);
			if (!sectionTail.ContainsKey(table))
			{
				if (output.Count > 0 && output[output.Count - 1].Length > 0)
				{
					output.Add("");
				}
				output.Add("[" + table + "]");
				output.Add(line);
				sectionTail[table] = output.Count - 1;
			}
			else
			{
				int insertAt = sectionTail[table] + 1;
				while (insertAt > 0 && output[insertAt - 1].Trim().Length == 0)
				{
					insertAt--;
				}
				output.Insert(insertAt, line);
				Shift(sectionTail, insertAt);
				sectionTail[table] = insertAt;
			}
		}
		StringBuilder builder = new StringBuilder();
		for (int i = 0; i < output.Count; i++)
		{
			if (i > 0)
			{
				builder.Append(newline);
			}
			builder.Append(output[i]);
		}
		if (trailing || output.Count > 0)
		{
			builder.Append(newline);
		}
		updated = builder.ToString();
		return true;
	}

	public static bool TryInspect(string line, string section, bool arrayTable, out string fullKey, out string value, out bool inline, out string nextSection, out bool nextArray, out string error)
	{
		fullKey = "";
		value = "";
		inline = false;
		nextSection = section;
		nextArray = arrayTable;
		if (!TryReadLine(line, section, arrayTable, out LineKind kind, out fullKey, out inline, out nextSection, out nextArray, out int valueStart, out int valueLength, out error))
		{
			return false;
		}
		if (kind == LineKind.Assignment)
		{
			value = line.Substring(valueStart, valueLength).Trim();
		}
		return true;
	}

	public static bool TryReadAssignment(string line, string section, out string fullKey, out string value, out bool inline, out string error)
	{
		fullKey = "";
		value = "";
		inline = false;
		if (!TryReadLine(line, section, false, out LineKind kind, out fullKey, out inline, out _, out _, out int valueStart, out int valueLength, out error))
		{
			return false;
		}
		if (kind != LineKind.Assignment)
		{
			error = "";
			return false;
		}
		value = line.Substring(valueStart, valueLength).Trim();
		return true;
	}

	private static bool TryReadLine(string line, string section, bool arrayTable, out LineKind kind, out string fullKey, out bool inline, out string nextSection, out bool nextArray, out int valueStart, out int valueLength, out string error)
	{
		kind = LineKind.Other;
		fullKey = "";
		inline = false;
		nextSection = section;
		nextArray = arrayTable;
		valueStart = 0;
		valueLength = 0;
		error = "";
		string trimmed = line.Trim();
		if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
		{
			return true;
		}
		if (trimmed.StartsWith("[", StringComparison.Ordinal))
		{
			return TryHeader(trimmed, out nextSection, out nextArray, out error) && (kind = LineKind.Header) == LineKind.Header;
		}
		int index = 0;
		while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
		{
			index++;
		}
		if (!TryKey(line, ref index, out string key, out error))
		{
			return false;
		}
		while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
		{
			index++;
		}
		if (index >= line.Length || line[index] != '=')
		{
			error = "TOML line is not a key: " + trimmed;
			return false;
		}
		index++;
		while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
		{
			index++;
		}
		if (!TryValue(line, ref index, out valueStart, out valueLength, out inline, out error))
		{
			return false;
		}
		fullKey = section.Length == 0 ? key : section + "." + key;
		kind = LineKind.Assignment;
		return true;
	}

	private static bool TryHeader(string trimmed, out string section, out bool arrayTable, out string error)
	{
		section = "";
		arrayTable = false;
		error = "";
		if (!trimmed.EndsWith("]", StringComparison.Ordinal))
		{
			error = "TOML table header is not closed: " + trimmed;
			return false;
		}
		string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
		if (inner.StartsWith("[", StringComparison.Ordinal))
		{
			arrayTable = true;
			if (!inner.EndsWith("]", StringComparison.Ordinal))
			{
				error = "TOML array table is not closed: " + trimmed;
				return false;
			}
			inner = inner.Substring(1, inner.Length - 2).Trim();
		}
		int index = 0;
		if (!TryKey(inner, ref index, out section, out error) || index != inner.Length)
		{
			error = error.Length == 0 ? "TOML table header is not a key: " + trimmed : error;
			return false;
		}
		return true;
	}

	private static bool TryKey(string text, ref int index, out string key, out string error)
	{
		key = "";
		error = "";
		List<string> parts = new List<string>();
		while (index < text.Length)
		{
			while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
			{
				index++;
			}
			if (index >= text.Length)
			{
				break;
			}
			char quote = text[index];
			if (quote == '"' || quote == '\'')
			{
				int start = index;
				index++;
				while (index < text.Length && text[index] != quote)
				{
					if (text[index] == '\\' && quote == '"')
					{
						index++;
					}
					if (index < text.Length)
					{
						index++;
					}
				}
				if (index >= text.Length || text[index] != quote)
				{
					error = "TOML quote is not closed.";
					return false;
				}
				index++;
				parts.Add(text.Substring(start, index - start));
			}
			else
			{
				int start = index;
				while (index < text.Length && text[index] != '.' && text[index] != '=' && text[index] != ' ' && text[index] != '\t' && text[index] != ']')
				{
					index++;
				}
				if (start == index)
				{
					error = "TOML key is empty.";
					return false;
				}
				parts.Add(text.Substring(start, index - start));
			}
			while (index < text.Length && (text[index] == ' ' || text[index] == '\t'))
			{
				index++;
			}
			if (index < text.Length && text[index] == '.')
			{
				index++;
				continue;
			}
			break;
		}
		if (parts.Count == 0)
		{
			error = "TOML key is empty.";
			return false;
		}
		key = string.Join(".", parts);
		return true;
	}

	private static bool TryValue(string line, ref int index, out int valueStart, out int valueLength, out bool inline, out string error)
	{
		valueStart = index;
		valueLength = 0;
		inline = false;
		error = "";
		if (index >= line.Length)
		{
			error = "TOML value is missing.";
			return false;
		}
		char c = line[index];
		if (c == '"' || c == '\'')
		{
			int start = index;
			index++;
			if (index + 1 < line.Length && line[index] == c && line[index + 1] == c)
			{
				error = "TOML multi-line values are read-only.";
				return false;
			}
			while (index < line.Length && line[index] != c)
			{
				if (line[index] == '\\' && c == '"')
				{
					index++;
				}
				if (index < line.Length)
				{
					index++;
				}
			}
			if (index >= line.Length || line[index] != c)
			{
				error = "TOML quote is not closed.";
				return false;
			}
			index++;
			valueLength = index - start;
			return true;
		}
		if (c == '{' || c == '[')
		{
			inline = true;
			char close = c == '{' ? '}' : ']';
			int depth = 0;
			int start = index;
			for (; index < line.Length; index++)
			{
				if (line[index] == '"' || line[index] == '\'')
				{
					char quote = line[index];
					index++;
					while (index < line.Length && line[index] != quote)
					{
						if (line[index] == '\\' && quote == '"')
						{
							index++;
						}
						index++;
					}
					continue;
				}
				if (line[index] == c)
				{
					depth++;
				}
				else if (line[index] == close)
				{
					depth--;
					if (depth == 0)
					{
						index++;
						valueLength = index - start;
						return true;
					}
				}
			}
			error = "TOML inline value is not closed.";
			return false;
		}
		int bare = index;
		while (index < line.Length && line[index] != '#')
		{
			index++;
		}
		while (index > bare && (line[index - 1] == ' ' || line[index - 1] == '\t'))
		{
			index--;
		}
		if (index == bare)
		{
			error = "TOML value is missing.";
			return false;
		}
		valueStart = bare;
		valueLength = index - bare;
		return true;
	}

	private static string ReplaceValue(string line, int valueStart, int valueLength, string replacement)
	{
		string previous = line.Substring(valueStart, valueLength);
		return line.Substring(0, valueStart) + FormatLike(previous, replacement) + line.Substring(valueStart + valueLength);
	}

	private static string FormatLike(string previous, string replacement)
	{
		if (previous.Length >= 2 && ((previous[0] == '"' && previous[previous.Length - 1] == '"') || (previous[0] == '\'' && previous[previous.Length - 1] == '\'')))
		{
			char quote = previous[0];
			return quote + replacement.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(quote.ToString(), "\\" + quote, StringComparison.Ordinal) + quote;
		}
		if (IsBare(replacement))
		{
			return replacement;
		}
		return "\"" + replacement.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
	}

	private static string FormatNewValue(string replacement)
	{
		return IsBare(replacement) ? replacement : "\"" + replacement.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
	}

	private static bool IsBare(string value)
	{
		if (value.Equals("true", StringComparison.Ordinal) || value.Equals("false", StringComparison.Ordinal))
		{
			return true;
		}
		if (value.Length == 0)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (!(char.IsDigit(c) || c == '.' || c == '-' || c == '+'))
			{
				return false;
			}
		}
		return true;
	}

	private static void SplitKey(string fullKey, out string table, out string leaf)
	{
		int split = fullKey.LastIndexOf('.');
		if (split < 0 || (split > 0 && (fullKey[split - 1] == '"' || fullKey[split - 1] == '\'')))
		{
			table = "";
			leaf = fullKey;
			return;
		}
		int quote = fullKey.IndexOf('"');
		if (quote >= 0 && quote < split)
		{
			table = fullKey.Substring(0, quote).TrimEnd('.');
			leaf = fullKey.Substring(quote);
			return;
		}
		quote = fullKey.IndexOf('\'');
		if (quote >= 0 && quote < split)
		{
			table = fullKey.Substring(0, quote).TrimEnd('.');
			leaf = fullKey.Substring(quote);
			return;
		}
		table = fullKey.Substring(0, split);
		leaf = fullKey.Substring(split + 1);
	}

	private static void Shift(Dictionary<string, int> tails, int insertAt)
	{
		List<string> keys = new List<string>(tails.Keys);
		for (int i = 0; i < keys.Count; i++)
		{
			if (tails[keys[i]] >= insertAt)
			{
				tails[keys[i]] = tails[keys[i]] + 1;
			}
		}
	}

	private enum LineKind
	{
		Other,
		Header,
		Assignment
	}
}
