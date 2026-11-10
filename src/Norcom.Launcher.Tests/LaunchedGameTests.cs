using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class LaunchedGameTests
{
	[Fact]
	public void PlayCaptionFollowsTheGameProcess()
	{
		Assert.Equal("Play", LaunchedGame.PlayCaption(false));
		Assert.Equal("Playing", LaunchedGame.PlayCaption(true));
	}

	[Fact]
	public void SelectPrefersThePrismChildThenTheInstance()
	{
		GameProcessRow other = new GameProcessRow("javaw.exe", "javaw minecraft other", 10, 1);
		GameProcessRow ours = new GameProcessRow("javaw.exe", "javaw -Dinstance=C:\\Prism\\instances\\NORCOM minecraft", 20, 77);
		GameProcessRow prism = new GameProcessRow("prismlauncher.exe", "prismlauncher --launch NORCOM", 77, 1);
		GameProcessRow idle = new GameProcessRow("javaw.exe", "javaw -version", 30, 77);
		int? child = PrismGameSignals.SelectGamePid(new[] { other, idle, prism, ours }, "instances\\NORCOM", 77);
		Assert.Equal(20, child);
		int? hinted = PrismGameSignals.SelectGamePid(new[] { other, ours }, "instances\\NORCOM", null);
		Assert.Equal(20, hinted);
		int? any = PrismGameSignals.SelectGamePid(new[] { idle, other }, null, null);
		Assert.Equal(10, any);
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { idle, prism }, "instances\\NORCOM", 77));
	}

	[Fact]
	public void LockRoundTripRemembersThePid()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-lock-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "game.lock");
		LaunchedGame.Remember(path, 4242);
		Assert.Equal(4242, StartupHeal.ReadGamePid(path));
		LaunchedGame.Forget(path);
		Assert.Null(StartupHeal.ReadGamePid(path));
		Assert.False(File.Exists(path));
	}

	[Fact]
	public void StopRefusesTheLauncherProcess()
	{
		Assert.False(LaunchedGame.TryStop(Environment.ProcessId));
		Assert.False(LaunchedGame.TryStop(0));
		Assert.False(LaunchedGame.TryStop(-4));
	}

	[Fact]
	public void StopOfAMissingProcessIsAlreadyDone()
	{
		int pid = 0;
		for (int candidate = 300000; candidate < 300080; candidate++)
		{
			if (!StartupHeal.IsAlive(candidate))
			{
				pid = candidate;
				break;
			}
		}
		Assert.True(pid > 0);
		Assert.False(StartupHeal.IsAlive(pid));
		// Without the recorded start time the stop is refused; with one, a process
		// that is already gone counts as stopped (no live process can be touched).
		Assert.False(LaunchedGame.TryStop(pid));
		Assert.True(LaunchedGame.TryStop(pid, DateTime.UtcNow.Ticks));
	}
}
