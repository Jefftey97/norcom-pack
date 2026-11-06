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
		GraphicsTier tier;
		if (hardware.Integrated || num < 4.0)
		{
			tier = GraphicsTier.Low;
		}
		else if (num < 6.0)
		{
			tier = GraphicsTier.Medium;
		}
		else if (num < 8.0)
		{
			tier = GraphicsTier.MediumHigh;
		}
		else if (num < 12.0)
		{
			tier = GraphicsTier.High;
		}
		else
		{
			tier = GraphicsTier.Ultra;
		}
		return Depth(tier, hardware);
	}

	public static GraphicsTier Depth(GraphicsTier tier, PcHardware hardware)
	{
		if (hardware.LogicalProcessors > 0 && hardware.LogicalProcessors < 4)
		{
			tier = Cap(tier, GraphicsTier.Medium);
		}
		else if (hardware.LogicalProcessors > 0 && hardware.LogicalProcessors < 6)
		{
			tier = Cap(tier, GraphicsTier.MediumHigh);
		}
		if (hardware.FreeRamBytes > 0 && hardware.FreeRamBytes < 4L * Gib)
		{
			tier = Drop(tier);
		}
		if (hardware.RefreshHz > 0 && hardware.RefreshHz < 60 && hardware.VramBytes > 0 && hardware.VramBytes < 6L * Gib)
		{
			tier = Cap(tier, GraphicsTier.Medium);
		}
		return tier;
	}

	private static GraphicsTier Cap(GraphicsTier tier, GraphicsTier ceiling)
	{
		if (tier > ceiling)
		{
			return ceiling;
		}
		return tier;
	}

	private static GraphicsTier Drop(GraphicsTier tier)
	{
		if (tier == GraphicsTier.Low)
		{
			return GraphicsTier.Low;
		}
		return tier - 1;
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
