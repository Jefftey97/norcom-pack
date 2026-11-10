using System;
using System.Collections.Generic;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class HardwareProbe
{
	public static Func<PcHardware?>? Reader { get; set; }

	public static long TryHostRamBytes()
	{
		try
		{
			foreach (Dictionary<string, object?> item in WmiQuery.Rows("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory") ?? new List<Dictionary<string, object?>>())
			{
				long ram = WmiQuery.Number(item, "TotalPhysicalMemory");
				if (ram > 0)
				{
					return ram;
				}
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("ram probe " + ex.GetType().Name);
		}
		return 0L;
	}

	public static PcHardware? TryRead()
	{
		if (Reader != null)
		{
			try
			{
				return Reader();
			}
			catch (Exception ex)
			{
				LauncherLog.Write("graphics probe " + ex.GetType().Name);
				return null;
			}
		}
		try
		{
			List<Dictionary<string, object?>>? adapters = WmiQuery.Rows("SELECT Name, AdapterRAM FROM Win32_VideoController", "Name", "AdapterRAM");
			if (adapters == null)
			{
				return null;
			}
			string? name = null;
			long vram = 0L;
			foreach (Dictionary<string, object?> item in adapters)
			{
				string gpu = WmiQuery.Text(item, "Name");
				if (gpu.Length == 0 || GraphicsAuto.IsIntegratedName(gpu))
				{
					continue;
				}
				long bytes = WmiQuery.Number(item, "AdapterRAM");
				if (bytes > vram)
				{
					name = gpu;
					vram = bytes;
				}
			}
			if (string.IsNullOrWhiteSpace(name))
			{
				return null;
			}
			long dedicated = GraphicsAuto.DedicatedVram(name, vram, vram);
			return new PcHardware(name, dedicated, TryHostRamBytes(), Environment.ProcessorCount, Integrated: false);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("graphics probe " + ex.GetType().Name);
			return null;
		}
	}
}
