using System;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public static class GraphicsAuto
{
	public const long Gib = 1073741824L;

	public static long DedicatedVram(string gpuName, long wmiBytes, long measuredBytes)
	{
		long reported = Math.Max(wmiBytes, measuredBytes);
		if (reported < 0)
		{
			reported = 0L;
		}
		if (!IsIntegratedName(gpuName))
		{
			long known = KnownDiscreteBytes(gpuName);
			if (known > reported)
			{
				return known;
			}
		}
		return reported;
	}

	public static long KnownDiscreteBytes(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return 0L;
		}
		string text = name.ToLowerInvariant();
		(string, int)[] array = new(string, int)[24]
		{
			("rtx 4090", 24),
			("rtx 4080", 16),
			("rtx 4070 ti", 12),
			("rtx 4070", 12),
			("rtx 4060 ti", 8),
			("rtx 4060", 8),
			("rtx 3090", 24),
			("rtx 3080 ti", 12),
			("rtx 3080", 10),
			("rtx 3070 ti", 8),
			("rtx 3070", 8),
			("rtx 3060 ti", 8),
			("rtx 3060", 12),
			("rx 7900", 20),
			("rx 7800", 16),
			("rx 7700", 12),
			("rx 7600", 8),
			("rx 6950", 16),
			("rx 6900", 16),
			("rx 6800", 16),
			("rx 6750", 12),
			("rx 6700", 12),
			("rx 6650", 8),
			("rx 6600", 8)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, int) tuple = array[i];
			if (text.Contains(tuple.Item1, StringComparison.Ordinal))
			{
				return (long)tuple.Item2 * 1073741824L;
			}
		}
		return 0L;
	}

	public static GraphicsTier Choose(PcHardware hardware)
	{
		if (MemoryPlan.Is3070Class(hardware.GpuName))
		{
			return GraphicsTier.High;
		}
		double num = (double)hardware.VramBytes / 1073741824.0;
		if (hardware.Integrated || num < 4.0)
		{
			return GraphicsTier.Low;
		}
		if (num < 6.0)
		{
			return GraphicsTier.Medium;
		}
		if (num < 8.0)
		{
			return GraphicsTier.MediumHigh;
		}
		if (num < 12.0)
		{
			return GraphicsTier.High;
		}
		return GraphicsTier.Ultra;
	}

	public static bool IsIntegratedName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string text = name.ToLowerInvariant();
		if (text.Contains("basic render") || text.Contains("microsoft basic"))
		{
			return true;
		}
		if (text.Contains("nvidia") || text.Contains("geforce") || text.Contains("quadro") || text.Contains("radeon rx") || text.Contains("arc a"))
		{
			return false;
		}
		if (!text.Contains("intel") && !text.Contains("uhd") && !text.Contains("iris") && !text.Contains("radeon(tm) graphics"))
		{
			return text.Contains("amd radeon graphics");
		}
		return true;
	}
}
