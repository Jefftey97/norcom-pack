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
		_ = HealIfDrifted(gameDir);
		LauncherLog.Write("play graphics kept custom settings");
		return tier;
	}

	public static bool HealIfDrifted(string gameDir)
	{
		_ = gameDir;
		return false;
	}

}
