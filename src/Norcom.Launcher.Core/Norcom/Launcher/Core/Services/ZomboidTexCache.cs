using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Norcom.Launcher.Core.Services;

/// <summary>
/// The server runs PzPerformanceFixes, whose texture cache keeps the graphics driver's compressed textures on disk so
/// later loads skip compressing them again. Its default 2048 MB limit is smaller than the server's mods need: the cache
/// fills, drops the oldest textures and misses on them at the next start, so almost every load compresses everything
/// again (measured on the owner's PC: about 66 hits out of 13,000 textures, 1 to 1.5 minutes per load).
/// On Play the launcher raises the limit in Zomboid/alexclientfixes-texcache.properties, and only raises it: a player
/// who turned the cache off (mode=off) or set a larger limit keeps their choice, every other line is kept, and nothing
/// changes when the disk lacks room for the larger cache plus a safety margin. Approved exception (Nov 25, 2026).
/// </summary>
public static class ZomboidTexCache
{
	public const string FileName = "alexclientfixes-texcache.properties";

	public const int TargetMb = 8192;

	public const int ModDefaultMb = 2048;

	public const long MarginBytes = 20L * 1024 * 1024 * 1024;

	public static string Apply(string userZomboidDir)
	{
		return Apply(userZomboidDir, FreeBytes);
	}

	public static string Apply(string userZomboidDir, Func<string, long> freeBytes)
	{
		if (string.IsNullOrWhiteSpace(userZomboidDir))
		{
			return "";
		}
		string path = Path.Combine(userZomboidDir, FileName);
		List<string> lines = new List<string>();
		if (File.Exists(path))
		{
			lines.AddRange(File.ReadAllLines(path));
		}
		int mbLine = -1;
		int currentMb = ModDefaultMb;
		for (int i = 0; i < lines.Count; i++)
		{
			string key = Key(lines[i], out string value);
			if (key == "mode" && value.Equals("off", StringComparison.OrdinalIgnoreCase))
			{
				return "";
			}
			if (key == "mb")
			{
				mbLine = i;
				if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mb) && mb > 0)
				{
					currentMb = mb;
				}
			}
		}
		if (currentMb >= TargetMb)
		{
			return "";
		}
		long needed = (long)(TargetMb - currentMb) * 1024 * 1024 + MarginBytes;
		long free;
		try
		{
			free = freeBytes(userZomboidDir);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return "";
		}
		if (free < needed)
		{
			return "";
		}
		string line = "mb=" + TargetMb.ToString(CultureInfo.InvariantCulture);
		if (mbLine >= 0)
		{
			lines[mbLine] = line;
		}
		else
		{
			lines.Add(line);
		}
		Directory.CreateDirectory(userZomboidDir);
		string temp = path + ".norcom-tmp";
		File.WriteAllText(temp, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(temp, path, overwrite: true);
		return "Texture cache for faster loading raised to " + (TargetMb / 1024).ToString(CultureInfo.InvariantCulture) + " GB.";
	}

	private static string Key(string line, out string value)
	{
		value = "";
		string text = line.Trim();
		if (text.Length == 0 || text.StartsWith('#') || text.StartsWith('!'))
		{
			return "";
		}
		int eq = text.IndexOf('=');
		if (eq <= 0)
		{
			return "";
		}
		value = text.Substring(eq + 1).Trim();
		return text.Substring(0, eq).Trim().ToLowerInvariant();
	}

	private static long FreeBytes(string dir)
	{
		string root = Path.GetPathRoot(Path.GetFullPath(dir)) ?? "";
		return root.Length == 0 ? 0 : new DriveInfo(root).AvailableFreeSpace;
	}
}
