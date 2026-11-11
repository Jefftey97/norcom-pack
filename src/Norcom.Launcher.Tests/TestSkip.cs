using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
	public WindowsFactAttribute()
	{
		if (!OperatingSystem.IsWindows())
		{
			Skip = "This check needs Windows file locking or WMI.";
		}
	}
}

public sealed class FixtureFactAttribute : FactAttribute
{
	public FixtureFactAttribute()
	{
		if (!FixturePresent())
		{
			Skip = "Set LAUNCHER_TEST_FIXTURE to the isolated UpdateFixture executable.";
		}
	}

	internal static bool FixturePresent()
	{
		string? fixture = Environment.GetEnvironmentVariable("LAUNCHER_TEST_FIXTURE");
		return !string.IsNullOrWhiteSpace(fixture) && File.Exists(fixture);
	}
}

public sealed class FixtureTheoryAttribute : TheoryAttribute
{
	public FixtureTheoryAttribute()
	{
		if (!FixtureFactAttribute.FixturePresent())
		{
			Skip = "Set LAUNCHER_TEST_FIXTURE to the isolated UpdateFixture executable.";
		}
	}
}
