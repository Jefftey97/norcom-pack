using System;
using System.Collections.Generic;

namespace Norcom.Launcher.Core.Text;

public static class TomlLite
{
	public sealed class Doc
	{
		public Dictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


		public List<Dictionary<string, string>> Files { get; } = new List<Dictionary<string, string>>();

	}

	public static Doc Parse(string text)
	{
		Doc doc = new Doc();
		Dictionary<string, string> dictionary = null;
		string text2 = "";
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text3 = StripComment(array[i]).Trim();
			if (text3.Length == 0)
			{
				continue;
			}
			if (text3.StartsWith("[[", StringComparison.Ordinal) && text3.EndsWith("]]", StringComparison.Ordinal))
			{
				string text4 = text3;
				string text5 = text4.Substring(2, text4.Length - 2 - 2).Trim();
				text2 = "";
				if (text5.Equals("files", StringComparison.OrdinalIgnoreCase))
				{
					dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					doc.Files.Add(dictionary);
				}
				else
				{
					dictionary = null;
				}
				continue;
			}
			if (text3.StartsWith('[') && text3.EndsWith(']'))
			{
				string text4 = text3;
				text2 = text4.Substring(1, text4.Length - 1 - 1).Trim();
				dictionary = null;
				continue;
			}
			int num = text3.IndexOf('=');
			if (num > 0)
			{
				string text6 = text3.Substring(0, num).Trim();
				string text4 = text3;
				int num2 = num + 1;
				string value = Unquote(text4.Substring(num2, text4.Length - num2).Trim());
				if (dictionary != null)
				{
					dictionary[text6] = value;
					continue;
				}
				string key = (string.IsNullOrEmpty(text2) ? text6 : (text2 + "." + text6));
				doc.Keys[key] = value;
				doc.Keys[text6] = value;
			}
		}
		return doc;
	}

	private static string StripComment(string line)
	{
		bool flag = false;
		char c = '\0';
		for (int i = 0; i < line.Length; i++)
		{
			char c2 = line[i];
			if (flag)
			{
				if (c2 == '\\' && i + 1 < line.Length)
				{
					i++;
				}
				else if (c2 == c)
				{
					flag = false;
				}
			}
			else if ((c2 == '"' || c2 == '\'') ? true : false)
			{
				flag = true;
				c = c2;
			}
			else if (c2 == '#')
			{
				return line.Substring(0, i);
			}
		}
		return line;
	}

	private static string Unquote(string value)
	{
		if (value.Length >= 2 && value[0] == '"')
		{
			if (value[value.Length - 1] == '"')
			{
				string text = value;
				return text.Substring(1, text.Length - 1 - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
			}
		}
		if (value.Length >= 2 && value[0] == '\'')
		{
			if (value[value.Length - 1] == '\'')
			{
				string text = value;
				return text.Substring(1, text.Length - 1 - 1);
			}
		}
		return value;
	}
}
