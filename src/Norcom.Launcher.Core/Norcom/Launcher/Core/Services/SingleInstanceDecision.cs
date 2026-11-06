namespace Norcom.Launcher.Core.Services;

public enum SingleInstanceDecision
{
	Continue,
	ExitBecauseOtherIsAlive,
	ReclaimedStuckLauncher
}
