using System;
using System.IO;
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
		if (seen3070)
		{
			tier = GraphicsTier.High;
		}
		else if (hardware == null)
		{
			tier = GraphicsTier.High;
		}
		else
		{
			tier = GraphicsAuto.Choose(hardware);
		}
		new GraphicsService().Apply(gameDir, tier);
		if (seen3070)
		{
			string text = File.ReadAllText(notePath);
			if (!text.Contains("sticky=3070", StringComparison.OrdinalIgnoreCase))
			{
				File.AppendAllText(notePath, "sticky=3070\n");
				string config = Path.Combine(gameDir, "config", "norcom-tier.txt");
				if (File.Exists(config))
				{
					File.AppendAllText(config, "sticky=3070\n");
				}
			}
		}
		return tier;
	}
}
