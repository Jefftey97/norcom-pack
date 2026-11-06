using Norcom.Launcher.Core.Models;
using Xunit;

namespace Norcom.Launcher.Tests;

public class CloseBehaviorTests
{
	[Fact]
	public void SavedCloseToTrayDoesNotComeBack()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-close-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "settings.json");
		File.WriteAllText(path, """{"CloseToTray":true,"closeToTray":true}""");
		LauncherSettings loaded = LauncherSettings.Load(path);
		Assert.False(loaded.CloseToTray);
		loaded.CloseToTray = true;
		loaded.Save(path);
		Assert.False(LauncherSettings.Load(path).CloseToTray);
		Assert.DoesNotContain("true", File.ReadAllText(path).Split("CloseToTray", 2)[1][..12], StringComparison.OrdinalIgnoreCase);
	}
}
