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
