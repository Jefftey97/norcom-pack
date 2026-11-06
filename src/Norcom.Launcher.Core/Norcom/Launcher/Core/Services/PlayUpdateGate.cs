using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class PlayUpdateGate
{
	public const string ResumeFileName = "play-after-update.flag";

	public static bool ExitAfterSchedule { get; set; } = true;

	public static async Task<bool> ApplyAsync(HttpClient http, CancellationToken ct)
	{
		return await new SelfUpdateService(http).TrySchedulePlayUpdateAsync(ct).ConfigureAwait(false);
	}

	public static void ExitIfScheduled()
	{
		if (!ExitAfterSchedule)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(NorcomPaths.LauncherDir);
			File.WriteAllText(Path.Combine(NorcomPaths.LauncherDir, ResumeFileName), "play" + Environment.NewLine);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play resume " + ex.GetType().Name);
		}
		Environment.Exit(0);
	}

	public static bool ConsumeResume()
	{
		string path = Path.Combine(NorcomPaths.LauncherDir, ResumeFileName);
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			File.Delete(path);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play resume " + ex.GetType().Name);
		}
		return true;
	}
}
