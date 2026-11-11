using System.IO;
using System.Runtime.CompilerServices;
using Norcom.Launcher.Core;

namespace Norcom.Launcher.UiTests;

internal static class TestIsolation
{
	[ModuleInitializer]
	internal static void UseTempState()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-ui-tests-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		string state = Path.Combine(root, "Local", "NORCOM");
		Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", state);
		Environment.SetEnvironmentVariable("NORCOM_GAME_DIR", Path.Combine(root, "game"));
		Environment.SetEnvironmentVariable("NORCOM_DESKTOP", Path.Combine(root, "Desktop"));
		Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE", "1");
		Directory.CreateDirectory(NorcomPaths.LauncherDir);
		Directory.CreateDirectory(NorcomPaths.GameDir);
	}
}
