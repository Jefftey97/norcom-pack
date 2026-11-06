using System;
using System.Reflection;
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
			Type? searcherType = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
			if (searcherType == null)
			{
				return 0L;
			}
			using IDisposable searcher = (IDisposable)Activator.CreateInstance(searcherType, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")!;
			object results = searcherType.GetMethod("Get")!.Invoke(searcher, null)!;
			foreach (object item in (System.Collections.IEnumerable)results)
			{
				PropertyInfo? indexer = item.GetType().GetProperty("Item");
				object? ram = indexer?.GetValue(item, new object[] { "TotalPhysicalMemory" });
				if (ram is IConvertible convertible)
				{
					return convertible.ToInt64(null);
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
			Type? searcherType = Type.GetType("System.Management.ManagementObjectSearcher, System.Management");
			if (searcherType == null)
			{
				return null;
			}
			string? name = null;
			long vram = 0L;
			using IDisposable searcher = (IDisposable)Activator.CreateInstance(searcherType, "SELECT Name, AdapterRAM FROM Win32_VideoController")!;
			object results = searcherType.GetMethod("Get")!.Invoke(searcher, null)!;
			foreach (object item in (System.Collections.IEnumerable)results)
			{
				PropertyInfo? indexer = item.GetType().GetProperty("Item");
				string gpu = indexer?.GetValue(item, new object[] { "Name" })?.ToString() ?? "";
				if (gpu.Length == 0 || GraphicsAuto.IsIntegratedName(gpu))
				{
					continue;
				}
				object? ram = indexer?.GetValue(item, new object[] { "AdapterRAM" });
				long bytes = ram is IConvertible convertible ? convertible.ToInt64(null) : 0L;
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
