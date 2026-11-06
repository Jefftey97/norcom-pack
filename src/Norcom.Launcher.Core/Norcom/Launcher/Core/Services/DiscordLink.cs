using System;

namespace Norcom.Launcher.Core.Services;

public static class DiscordLink
{
	public const string DefaultInvite = "https://discord.gg/hS5WmktQNz";

	public static string? Read(string? fileText)
	{
		if (string.IsNullOrWhiteSpace(fileText))
		{
			return null;
		}
		string[] array = fileText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.StartsWith('#'))
			{
				continue;
			}
			if (text.StartsWith("DISCORD_URL=", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = text;
				int length = "DISCORD_URL=".Length;
				string text3 = text2.Substring(length, text2.Length - length).Trim();
				if (!IsInvite(text3))
				{
					return null;
				}
				return text3;
			}
			if (IsInvite(text))
			{
				return text;
			}
		}
		return null;
	}

	public static bool IsInvite(string? url)
	{
		if (!string.IsNullOrWhiteSpace(url))
		{
			if (!url.StartsWith("https://discord.gg/", StringComparison.OrdinalIgnoreCase))
			{
				return url.StartsWith("https://discord.com/invite/", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	public static string? Choose(string? packFileText, string? settingsUrl, string? environmentUrl, string? localFileText, string? legacyLauncherFileText = null)
	{
		return Read(packFileText) ?? (IsInvite(settingsUrl) ? settingsUrl.Trim() : null) ?? (IsInvite(environmentUrl) ? environmentUrl.Trim() : null) ?? Read(localFileText) ?? Read(legacyLauncherFileText) ?? "https://discord.gg/hS5WmktQNz";
	}
}
