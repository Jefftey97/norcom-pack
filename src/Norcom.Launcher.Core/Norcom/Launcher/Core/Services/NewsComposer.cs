using System;
using System.CodeDom.Compiler;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace Norcom.Launcher.Core.Services;

public static class NewsComposer
{
	public static string Compose(string launcherVersion, string? newsMarkdown, string? packNotes, string? packVersion)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("Launcher ").Append(launcherVersion).AppendLine();
		stringBuilder.AppendLine("Native Windows build for the NORCOM SERVER.");
		if (!string.IsNullOrWhiteSpace(packVersion))
		{
			stringBuilder.Append("Pack ").Append(packVersion.Trim()).AppendLine();
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Pack notes");
		AppendFiltered(stringBuilder, packNotes, launcherVersion);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Server notes");
		AppendFiltered(stringBuilder, newsMarkdown, launcherVersion);
		return stringBuilder.ToString().TrimEnd() + Environment.NewLine;
	}

	private static void AppendFiltered(StringBuilder sb, string? markdown, string launcherVersion)
	{
		if (string.IsNullOrWhiteSpace(markdown))
		{
			sb.AppendLine("Nothing posted yet.");
			return;
		}
		string[] array = markdown.Replace("\r\n", "\n").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].TrimEnd();
			if (!text.StartsWith('#') && !IsStaleLauncherLine(text, launcherVersion))
			{
				if (string.IsNullOrWhiteSpace(text))
				{
					sb.AppendLine();
				}
				else
				{
					sb.AppendLine(text);
				}
			}
		}
	}

	public static bool IsStaleLauncherLine(string line, string launcherVersion)
	{
		Match match = LauncherVer().Match(line);
		if (!match.Success)
		{
			return false;
		}
		return VersionCompare.Compare(match.Groups[1].Value, launcherVersion) < 0;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex LauncherVer()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__LauncherVer_1.Instance;
	}
}
