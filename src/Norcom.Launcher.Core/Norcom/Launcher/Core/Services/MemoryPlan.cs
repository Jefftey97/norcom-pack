using System;

namespace Norcom.Launcher.Core.Services;

public static class MemoryPlan
{
	public const int HeadroomMb = 8192;

	public const int LegacyCeilingMb = 6144;

	public static int ForPlay(int savedMb, long hostRamBytes, string? gpuName)
	{
		int hostMb = 0;
		if (hostRamBytes > 0)
		{
			hostMb = (int)Math.Min(int.MaxValue, hostRamBytes / (1024L * 1024L));
		}
		int target = savedMb <= 0 ? HeadroomMb : savedMb;
		bool lowHeap = target <= LegacyCeilingMb;
		bool card = Is3070Class(gpuName);
		if (lowHeap || card)
		{
			target = Math.Max(target, HeadroomMb);
		}
		int cap = hostMb == 0 ? 10240 : Math.Clamp(hostMb - 6144, 4096, 10240);
		return Math.Clamp(target, 2048, cap);
	}

	public static bool Is3070Class(string? gpuName)
	{
		return !string.IsNullOrWhiteSpace(gpuName) && gpuName.Contains("3070", StringComparison.OrdinalIgnoreCase);
	}
}
