using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Services;

public sealed class WmiPcProbe
{
	public PcHardware? TryRead()
	{
		try
		{
			List<(string, long, bool)> list = ReadGpus();
			List<(string, long)> list2 = DxgiVram.Read().Concat(RegistryVram.Read()).ToList();
			if (list.Count == 0 && list2.Count == 0)
			{
				return null;
			}
			List<(string, long, bool)> list3 = new List<(string, long, bool)>();
			foreach (var gpu in list)
			{
				long measuredBytes = (from tuple2 in list2
					where NamesMatch(gpu.Item1, tuple2.Item1)
					select tuple2.Item2).DefaultIfEmpty(0L).Max();
				long item = GraphicsAuto.DedicatedVram(gpu.Item1, gpu.Item2, measuredBytes);
				list3.Add((gpu.Item1, item, gpu.Item3));
			}
			foreach (var item2 in list2.Where(tuple2 => tuple2.Item2 > 4294967296L && !GraphicsAuto.IsIntegratedName(tuple2.Item1)))
			{
				if (!list3.Any<(string, long, bool)>(((string Name, long Vram, bool Integrated) tuple2) => NamesMatch(tuple2.Name, item2.Item1)))
				{
					list3.Add((item2.Item1, item2.Item2, false));
				}
			}
			if (list3.Count == 0)
			{
				return null;
			}
			(string, long, bool) tuple = (from tuple2 in list3
				orderby (!tuple2.Item3) ? 1 : 0 descending, tuple2.Item2 descending
				select tuple2).First();
			return new PcHardware(tuple.Item1, tuple.Item2, ReadRam(), ReadThreads(), tuple.Item3, ReadFreeRam(), ReadRefreshHz(tuple.Item1));
		}
		catch
		{
			return null;
		}
	}

	private static List<(string Name, long Vram, bool Integrated)> ReadGpus()
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		List<(string, long, bool)> list = new List<(string, long, bool)>();
		ManagementObjectSearcher val = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
		try
		{
			var enumerator = val.Get().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					ManagementObject val2 = (ManagementObject)enumerator.Current;
					string text = ((ManagementBaseObject)val2)["Name"]?.ToString() ?? "";
					if (text.Length != 0)
					{
						list.Add((text, ToLong(((ManagementBaseObject)val2)["AdapterRAM"]), GraphicsAuto.IsIntegratedName(text)));
					}
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return list;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static long ReadRam()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Expected O, but got Unknown
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		ManagementObjectSearcher val = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
		try
		{
			var enumerator = val.Get().GetEnumerator();
			try
			{
				if (enumerator.MoveNext())
				{
					return ToLong(((ManagementBaseObject)(ManagementObject)enumerator.Current)["TotalPhysicalMemory"]);
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return 0L;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static long ReadFreeRam()
	{
		try
		{
			ManagementObjectSearcher val = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
			try
			{
				var enumerator = val.Get().GetEnumerator();
				try
				{
					if (enumerator.MoveNext())
					{
						long kb = ToLong(((ManagementBaseObject)(ManagementObject)enumerator.Current)["FreePhysicalMemory"]);
						if (kb > 0)
						{
							return kb * 1024L;
						}
					}
				}
				finally
				{
					((IDisposable)enumerator)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
		}
		return 0L;
	}

	private static int ReadRefreshHz(string gpuName)
	{
		try
		{
			ManagementObjectSearcher val = new ManagementObjectSearcher("SELECT Name, CurrentRefreshRate FROM Win32_VideoController");
			try
			{
				int best = 0;
				int named = 0;
				var enumerator = val.Get().GetEnumerator();
				try
				{
					while (enumerator.MoveNext())
					{
						ManagementObject item = (ManagementObject)enumerator.Current;
						int hz = (int)ToLong(((ManagementBaseObject)item)["CurrentRefreshRate"]);
						if (hz <= 1)
						{
							continue;
						}
						string name = ((ManagementBaseObject)item)["Name"]?.ToString() ?? "";
						if (hz > best)
						{
							best = hz;
						}
						if (name.Length > 0 && (name.Contains(gpuName, StringComparison.OrdinalIgnoreCase) || gpuName.Contains(name, StringComparison.OrdinalIgnoreCase)) && hz > named)
						{
							named = hz;
						}
					}
				}
				finally
				{
					((IDisposable)enumerator)?.Dispose();
				}
				return named > 0 ? named : best;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
			return 0;
		}
	}

	private static int ReadThreads()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Expected O, but got Unknown
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Expected O, but got Unknown
		int num = 0;
		ManagementObjectSearcher val = new ManagementObjectSearcher("SELECT NumberOfLogicalProcessors FROM Win32_Processor");
		try
		{
			var enumerator = val.Get().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					ManagementObject val2 = (ManagementObject)enumerator.Current;
					num += (int)ToLong(((ManagementBaseObject)val2)["NumberOfLogicalProcessors"]);
				}
			}
			finally
			{
				((IDisposable)enumerator)?.Dispose();
			}
			return num;
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static bool NamesMatch(string left, string right)
	{
		if (!left.Contains(right, StringComparison.OrdinalIgnoreCase))
		{
			return right.Contains(left, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static long ToLong(object? value)
	{
		if (!(value is uint num))
		{
			if (!(value is int num2))
			{
				if (!(value is long result))
				{
					if (value is ulong num3)
					{
						return (long)((num3 > long.MaxValue) ? long.MaxValue : num3);
					}
					return 0L;
				}
				return result;
			}
			return num2;
		}
		return num;
	}
}
