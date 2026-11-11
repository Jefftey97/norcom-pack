using Norcom.Launcher.Core.Updates;

namespace Norcom.Launcher.Core.Games.Minecraft;

public static class MinecraftRunGate
{
	public static bool Running(string lockPath)
	{
		try
		{
			return !string.IsNullOrWhiteSpace(lockPath) && GameUpdateGuard.IsBlocked(lockPath);
		}
		catch
		{
			return true;
		}
	}
}
