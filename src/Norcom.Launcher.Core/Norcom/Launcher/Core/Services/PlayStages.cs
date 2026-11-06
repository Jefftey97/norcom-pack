using System;

namespace Norcom.Launcher.Core.Services;

public static class PlayStages
{
	public const string Variable = "NORCOM_STOP_AFTER";

	public static bool StopBeforeLaunch(bool disposableProfile, string? requested, string stage)
	{
		if (!disposableProfile || string.IsNullOrWhiteSpace(requested))
		{
			return false;
		}
		return requested.Trim().Equals(stage, StringComparison.OrdinalIgnoreCase);
	}
}
