using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class GraphicsPlay
{
	public static Func<PcHardware?>? Probe { get; set; }

	public static GraphicsTier ApplyAuto(string gameDir)
	{
		PcHardware? hardware = null;
		try
		{
			hardware = (Probe ?? HardwareProbe.TryRead)();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("graphics probe " + ex.GetType().Name);
		}
		string notePath = Path.Combine(gameDir, "norcom-graphics-tier.txt");
		string previous = File.Exists(notePath) ? File.ReadAllText(notePath) : "";
		bool saved3070 = previous.Contains("sticky=3070", StringComparison.OrdinalIgnoreCase);
		bool seen3070 = saved3070 || (hardware != null && MemoryPlan.Is3070Class(hardware.GpuName));
		GraphicsTier tier;
		if (seen3070 || hardware == null)
		{
			tier = GraphicsTier.High;
		}
		else
		{
			tier = GraphicsAuto.Choose(hardware);
		}
		bool healed = HealIfDrifted(gameDir);
		WriteTierNote(gameDir, tier, seen3070);
		if (!healed)
		{
			LauncherLog.Write("play graphics kept custom settings");
		}
		return tier;
	}

	public static bool HealIfDrifted(string gameDir)
	{
		_ = gameDir;
		return false;
	}

	private static void WriteTierNote(string gameDir, GraphicsTier tier, bool seen3070)
	{
		string text = "tier=" + tier + "\nappliedUtc=" + DateTimeOffset.UtcNow.ToString("o") + "\n";
		if (seen3070)
		{
			text += "sticky=3070\n";
		}
		string notePath = Path.Combine(gameDir, "norcom-graphics-tier.txt");
		File.WriteAllText(notePath, text);
		string config = Path.Combine(gameDir, "config");
		Directory.CreateDirectory(config);
		File.WriteAllText(Path.Combine(config, "norcom-tier.txt"), text);
	}

}
