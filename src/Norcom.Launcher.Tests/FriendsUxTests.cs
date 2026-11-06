using Xunit;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Tests;

public class FriendsUxTests
{
	[Fact]
	public void MissingStatusFileIsUnknown()
	{
		HomeStatus status = HomeStatusClient.ReadFile(Path.Combine(Path.GetTempPath(), "norcom-missing-" + Guid.NewGuid().ToString("n") + ".json"));
		Assert.False(status.ShowOnline);
		Assert.Equal("Unknown", status.Headline);
		Assert.Equal("—", status.PlayersText);
		Assert.Contains("not published", status.Detail);
		Assert.Contains(HomeStatusClient.HostStatusPath, status.Source);
	}

	[Fact]
	public void StatusFileShowsUpAndPlayerCountOnly()
	{
		string path = Path.Combine(Path.GetTempPath(), "norcom-status-" + Guid.NewGuid().ToString("n") + ".json");
		File.WriteAllText(path, """{"up":true,"players":4,"updatedUtc":"2026-11-06T03:00:00Z","mspt":99}""");
		HomeStatus status = HomeStatusClient.ReadFile(path);
		Assert.True(status.ShowOnline);
		Assert.Equal("Online", status.Headline);
		Assert.Equal("4 online", status.PlayersText);
		Assert.DoesNotContain("mspt", status.Detail);
		Assert.DoesNotContain("99", status.PlayersText);
		Assert.Contains("2026-11-06T03:00:00Z", status.Source);
	}

	[Fact]
	public void StatusDownIsOfflineWithoutACount()
	{
		HomeStatus status = HomeStatusClient.Parse("""{"up":false,"players":4,"updatedUtc":"2026-11-06T03:00:00Z"}""", "status.json");
		Assert.False(status.ShowOnline);
		Assert.Equal("Offline", status.Headline);
		Assert.Equal("—", status.PlayersText);
	}

	[Fact]
	public void BadStatusJsonFailsSoft()
	{
		HomeStatus status = HomeStatusClient.Parse("not-json", "status.json");
		Assert.Equal("Unknown", status.Headline);
		Assert.Contains("could not be read", status.Detail);
	}

	[Fact]
	public void RepairNamesThePackResyncAndKeepsThePinSentence()
	{
		string text = RepairTrace.Fail("pack resync", PinGate.BlockCopy);
		Assert.Contains("pack resync", text);
		Assert.Contains(PinGate.BlockCopy, text);
		Assert.Contains("Worlds", RepairTrace.SuccessLine());
	}

	[Fact]
	public void JavaFailureIsNamedOnTheRepairLine()
	{
		string line = RepairTrace.SuccessLine("the java path is missing");
		Assert.Contains("Java check failed", line);
		Assert.Contains("the java path is missing", line);
		Assert.Contains("Worlds were not wiped", line);
	}

	[Fact]
	public void UpdateNotesStayReadableAfterTheyAreSaved()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-notes-" + Guid.NewGuid().ToString("n"));
		UpdateNotes.Remember(dir, new LauncherManifest
		{
			Version = "2.0.30",
			Notes = "Self-heal on pin mismatch. Auto Fabulous High."
		}, "2.0.27");
		string? section = UpdateNotes.Section(dir, "2.0.27");
		Assert.NotNull(section);
		Assert.Contains("2.0.30 is available", section);
		Assert.Contains("Self-heal on pin mismatch", section);
		string? installed = UpdateNotes.Section(dir, "2.0.30");
		Assert.Contains("Launcher update notes", installed);
	}
}
