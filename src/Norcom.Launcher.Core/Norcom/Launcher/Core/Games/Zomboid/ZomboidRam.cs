using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Zomboid;

// Memory Project Zomboid gets on Play. The slider's saved value is sent on every Play.
// A player who never moved the slider gets a recommended default instead of the game's stock 3072m.
public static class ZomboidRam
{
	public const int LargeHostDefaultMb = 8192;

	public const int SmallHostDefaultMb = 6144;

	public const long LargeHostThresholdBytes = 16L * 1024L * 1024L * 1024L;

	public static int DefaultRequestMb(long totalBytes)
	{
		return totalBytes > LargeHostThresholdBytes ? LargeHostDefaultMb : SmallHostDefaultMb;
	}

	public static RamBudget Budget(ZomboidState state, long totalBytes, long freeBytes)
	{
		int requested = state != null && state.RamSpecified && state.RamMb > 0 ? state.RamMb : DefaultRequestMb(totalBytes);
		return RamBudget.Create(totalBytes, freeBytes, requested);
	}

	public static int HeapForPlay(ZomboidState state, long totalBytes, long freeBytes)
	{
		return Budget(state, totalBytes, freeBytes).ChosenMb;
	}
}
