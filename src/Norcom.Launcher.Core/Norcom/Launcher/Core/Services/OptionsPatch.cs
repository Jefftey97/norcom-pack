using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class OptionsPatch
{
	public static string Apply(string? existing, IReadOnlyDictionary<string, string> updates)
	{
		string value = ((existing != null && existing.Contains("\r\n", StringComparison.Ordinal)) ? "\r\n" : "\n");
		List<string> list = (string.IsNullOrEmpty(existing) ? new List<string>() : existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList());
		if (list.Count > 0)
		{
			if (list[list.Count - 1].Length == 0)
			{
				list.RemoveAt(list.Count - 1);
			}
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < list.Count; i++)
		{
			string text = list[i];
			if (text.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			int num = text.IndexOf(':');
			if (num > 0)
			{
				string text2 = text.Substring(0, num);
				if (updates.TryGetValue(text2, out string value2))
				{
					list[i] = text2 + ":" + value2;
					hashSet.Add(text2);
				}
			}
		}
		foreach (KeyValuePair<string, string> update in updates)
		{
			if (!hashSet.Contains(update.Key))
			{
				list.Add(update.Key + ":" + update.Value);
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in list)
		{
			stringBuilder.Append(item);
			stringBuilder.Append(value);
		}
		return stringBuilder.ToString();
	}

	public static string ApplyProperties(string? existing, IReadOnlyDictionary<string, string> updates)
	{
		string value = ((existing != null && existing.Contains("\r\n", StringComparison.Ordinal)) ? "\r\n" : "\n");
		List<string> list = (string.IsNullOrEmpty(existing) ? new List<string>() : existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList());
		if (list.Count > 0)
		{
			if (list[list.Count - 1].Length == 0)
			{
				list.RemoveAt(list.Count - 1);
			}
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < list.Count; i++)
		{
			string text = list[i].Trim();
			if (text.StartsWith('#') || text.Length == 0)
			{
				continue;
			}
			int num = text.IndexOf('=');
			if (num > 0)
			{
				string text2 = text.Substring(0, num).Trim();
				if (updates.TryGetValue(text2, out string value2))
				{
					list[i] = text2 + "=" + value2;
					hashSet.Add(text2);
				}
			}
		}
		foreach (KeyValuePair<string, string> update in updates)
		{
			if (!hashSet.Contains(update.Key))
			{
				list.Add(update.Key + "=" + update.Value);
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string item in list)
		{
			stringBuilder.Append(item);
			stringBuilder.Append(value);
		}
		return stringBuilder.ToString();
	}
}
