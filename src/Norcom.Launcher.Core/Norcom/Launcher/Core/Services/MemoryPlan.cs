using System;

namespace Norcom.Launcher.Core.Services;

public static class MemoryPlan
{
	public const int HeadroomMb = 8192;

	public const int LegacyCeilingMb = 6144;

	public static int ForPlay(int savedMb, long hostRamBytes, string? gpuName)
	{
		_ = gpuName;
		return RamBudget.Create(hostRamBytes, 0L, savedMb).ChosenMb;
	}

	public static bool Is3070Class(string? gpuName)
	{
		return !string.IsNullOrWhiteSpace(gpuName) && gpuName.Contains("3070", StringComparison.OrdinalIgnoreCase);
	}
}
