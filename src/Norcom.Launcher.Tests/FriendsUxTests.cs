using Xunit;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Tests;

public class FriendsUxTests
{
	[Fact]
	public void ServerListPingOnlineShowsThePlayerCount()
	{
		HomeStatus status = HomeStatusClient.FromPing(new ServerStatus(Online: true, 4, 20, "secret motd", 12, new[] { "Alice" }, ""));
		Assert.True(status.ShowOnline);
		Assert.Equal("Online", status.Headline);
		Assert.Equal("4 online", status.PlayersText);
		Assert.Equal(FriendsServer.FallbackDisplay, status.Source);
		Assert.DoesNotContain(":25565", status.Source);
		Assert.DoesNotContain("Alice", status.Detail);
		Assert.DoesNotContain("secret", status.Detail);
		Assert.DoesNotContain("not published", status.Detail);
	}

	[Fact]
	public void ServerListPingTimeoutIsOffline()
	{
		HomeStatus status = HomeStatusClient.FromPing(new ServerStatus(Online: false, 0, 0, "", -1, Array.Empty<string>(), "Timed out reaching the server."));
		Assert.False(status.ShowOnline);
		Assert.Equal("Offline", status.Headline);
		Assert.Equal("—", status.PlayersText);
		Assert.Equal("Timed out reaching the server.", status.Detail);
		Assert.DoesNotContain("not published", status.Detail);
		Assert.Equal(FriendsServer.FallbackDisplay, status.Source);
		Assert.DoesNotContain(":25565", status.Source);
	}

	[Fact]
	public void PresentStatusFileOverridesThePing()
	{
		string path = Path.Combine(Path.GetTempPath(), "norcom-status-" + Guid.NewGuid().ToString("n") + ".json");
		File.WriteAllText(path, """{"up":true,"players":2,"updatedUtc":"2026-11-06T03:00:00Z"}""");
		HomeStatus? file = HomeStatusClient.ReadFile(path);
		Assert.NotNull(file);
		Assert.Equal("2 online", file.PlayersText);
		Assert.Null(HomeStatusClient.ReadFile(path + ".missing"));
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
	public void BadStatusJsonIsNotAnOverride()
	{
		string path = Path.Combine(Path.GetTempPath(), "norcom-bad-" + Guid.NewGuid().ToString("n") + ".json");
		File.WriteAllText(path, "not-json");
		Assert.Null(HomeStatusClient.ReadFile(path));
	}

	[Fact]
	public void RepairNamesThePackResyncAndKeepsThePinSentence()
	{
		string text = RepairTrace.Fail("pack resync", PinGate.BlockCopy);
		Assert.Contains("pack resync", text);
		Assert.Contains(PinGate.BlockCopy, text);
		Assert.DoesNotContain("Repair", text);
		Assert.DoesNotContain("Repair", PinGate.BlockCopy);
		Assert.Contains("press Play again", PinGate.BlockCopy);
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
	public async Task RepairNamesJavaThenThePack()
	{
		TransferMeter meter = new TransferMeter();
		string? stage = null;
		string line = await RepairRun.ExecuteAsync(meter, _ =>
		{
			stage = RepairTrace.Stage;
			return Task.CompletedTask;
		}, _ => throw new InvalidOperationException("the java path is missing"), CancellationToken.None);
		Assert.Equal("pack resync", stage);
		Assert.Contains("Java check failed", line);
		Assert.Contains("the java path is missing", line);
		Assert.Contains("Worlds were not wiped", line);
		Assert.Equal("Repair finished", meter.Snapshot().Label);
	}

	[Fact]
	public void PayloadStampMustMatchTheProductVersion()
	{
		Assert.Equal("2.0.40", PayloadStamp.ForProduct("2.0.40.0"));
		Assert.True(PayloadStamp.Identical("2.0.40", "2.0.40+build"));
		Assert.False(PayloadStamp.Identical("2.0.39", "2.0.40"));
		string sha = new string('b', 64);
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.39", "2.0.40", "2.0.39", "2.0.40", null, sha));
		Assert.Equal(UpdateReport.PayloadVersion, UpdateReport.Rejection("2.0.39", "2.0.40", "2.0.40", "2.0.39", null, sha));
		Assert.Null(UpdateReport.Rejection("2.0.39", "2.0.40", "2.0.40", "2.0.40", null, sha));
		string dir = Path.Combine(Path.GetTempPath(), "norcom-stamp-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string exe = Path.Combine(dir, "NorcomLauncher.exe");
		File.WriteAllText(exe, "exe");
		PayloadStamp.WriteBeside(exe, "2.0.40");
		Assert.Equal("2.0.40", File.ReadAllText(Path.Combine(dir, PayloadStamp.FileName)).Trim());
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
