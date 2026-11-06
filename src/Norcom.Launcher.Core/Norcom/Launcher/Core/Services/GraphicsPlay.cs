using System;
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
		GraphicsTier tier = (hardware == null) ? GraphicsTier.High : GraphicsAuto.Choose(hardware);
		new GraphicsService().Apply(gameDir, tier);
		return tier;
	}
}
