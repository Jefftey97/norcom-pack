using System;
using System.Globalization;
using System.IO;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Minecraft;

public static class MinecraftRam
{
	public const int PrismDefaultMb = 1024;

	public static string InstanceCfg(string appData)
	{
		return Path.Combine(PrismCommand.DefaultDataDir(appData), "instances", PrismCommand.InstanceId, "instance.cfg");
	}

	public static string GlobalCfg(string appData)
	{
		return Path.Combine(PrismCommand.DefaultDataDir(appData), "prismlauncher.cfg");
	}

	// Memory the player set in Prism: the NORCOM instance override, else Prism's global setting. 0 when never set.
	public static int ReadPlayerSetMb(string instanceCfgPath, string globalCfgPath)
	{
		if (string.Equals(ReadKey(instanceCfgPath, "OverrideMemory"), "true", StringComparison.OrdinalIgnoreCase))
		{
			int instance = ParseMb(ReadKey(instanceCfgPath, "MaxMemAlloc"));
			if (instance > 0)
			{
				return instance;
			}
		}
		return ParseMb(ReadKey(globalCfgPath, "MaxMemAlloc"));
	}

	private static int ParseMb(string? text)
	{
		return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mb) && mb > 0 ? mb : 0;
	}

	private static string? ReadKey(string path, string wanted)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
				{
					continue;
				}
				int split = line.IndexOf('=');
				if (split > 0 && line.Substring(0, split).Trim().Equals(wanted, StringComparison.Ordinal))
				{
					return line.Substring(split + 1).Trim();
				}
			}
		}
		catch (IOException)
		{
			return null;
		}
		return null;
	}

	public static int ReadMaxMb(string instanceCfgPath)
	{
		if (string.IsNullOrWhiteSpace(instanceCfgPath) || !File.Exists(instanceCfgPath))
		{
			return PrismDefaultMb;
		}
		try
		{
			string[] lines = File.ReadAllLines(instanceCfgPath);
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
				{
					continue;
				}
				int split = line.IndexOf('=');
				if (split <= 0)
				{
					continue;
				}
				string key = line.Substring(0, split).Trim();
				if (!key.Equals("MaxMemAlloc", StringComparison.Ordinal))
				{
					continue;
				}
				if (int.TryParse(line.Substring(split + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mb) && mb > 0)
				{
					return mb;
				}
			}
		}
		catch (IOException)
		{
			return PrismDefaultMb;
		}
		return PrismDefaultMb;
	}
}
