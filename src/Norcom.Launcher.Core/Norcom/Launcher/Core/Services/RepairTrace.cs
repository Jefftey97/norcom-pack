using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class RepairTrace
{
	public static string Stage { get; private set; } = "";

	public static string? JavaNote { get; private set; }

	public static void Begin()
	{
		Stage = "pin check";
		JavaNote = null;
	}

	public static void SetStage(string stage)
	{
		Stage = stage;
		LauncherLog.Write("repair stage " + stage);
	}

	public static void NoteJava(string detail)
	{
		JavaNote = detail.Trim();
		LauncherLog.Write("repair java " + JavaNote);
	}

	public static string Fail(string stage, string why)
	{
		SetStage(stage);
		return "Could not finish " + stage + ". " + why.Trim();
	}

	public static string SuccessLine()
	{
		return SuccessLine(JavaNote);
	}

	public static string SuccessLine(string? javaNote)
	{
		if (!string.IsNullOrWhiteSpace(javaNote))
		{
			return "Pack resync finished. Java check failed: " + javaNote.Trim() + " Worlds were not wiped.";
		}
		return "Pack files were checked. Worlds, keybinds, servers, and screenshots were left in place.";
	}
}
