using System.Diagnostics;
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
		const string serverLine = @"G:\NORCOM\Server\java\jdk-25.0.5+8\bin\java.exe -Xms8G -Xmx8G -jar G:\NORCOM\Server\fabric-server-launch.jar nogui";
		GameProcessRow other = new GameProcessRow("javaw.exe", "javaw minecraft other", 10, 1);
		GameProcessRow ours = new GameProcessRow("javaw.exe", "javaw -Dinstance=C:\\Prism\\instances\\NORCOM net.fabricmc.loader.impl.launch.knot.KnotClient", 20, 77);
		GameProcessRow prism = new GameProcessRow("prismlauncher.exe", "prismlauncher --launch NORCOM", 77, 1);
		GameProcessRow idle = new GameProcessRow("javaw.exe", "javaw -version", 30, 77);
		GameProcessRow server = new GameProcessRow("java.exe", serverLine, 30920, 4);
		int? child = PrismGameSignals.SelectGamePid(new[] { other, idle, prism, server, ours }, "instances\\NORCOM", 77);
		Assert.Equal(20, child);
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { other, server, ours }, "instances\\NORCOM", null));
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { idle, other, server }, null, null));
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { idle, prism, server }, "instances\\NORCOM", 77));
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { server, ours }, null, null, "instances\\NORCOM"));
	}

	[Fact]
	public void ServerJavaLockIsDroppedWithoutStoppingTheProcess()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-lock-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "game.lock");
		using Process process = Process.GetCurrentProcess();
		StartupHeal.WriteGameLock(path, process.Id);
		Assert.True(StartupHeal.GameLockAlive(path));
		StartupHeal.CommandReader = pid => pid == process.Id
			? ("java.exe", @"G:\NORCOM\Server\java\jdk-25.0.5+8\bin\java.exe -Xms8G -Xmx8G -jar G:\NORCOM\Server\fabric-server-launch.jar nogui")
			: null;
		try
		{
			Assert.True(StartupHeal.ReleaseForeignClientLock(path));
			Assert.False(File.Exists(path));
			Assert.False(process.HasExited);
		}
		finally
		{
			StartupHeal.CommandReader = null;
		}
	}

	[Fact]
	public void EmptyCommandLineKeepsTheMatchingLock()
	{
		Assert.False(PrismGameSignals.ShouldForgetLockedProcess("java.exe", ""));
		Assert.False(PrismGameSignals.ShouldForgetLockedProcess("java.exe", null));
		Assert.True(PrismGameSignals.ShouldForgetLockedProcess("chrome.exe", "\"C:\\Program Files\\Google\\Chrome\\chrome.exe\" --type=renderer"));
		Assert.True(PrismGameSignals.ShouldForgetLockedProcess("chrome.exe", null));
		Assert.True(PrismGameSignals.ShouldForgetLockedProcess("ProjectZomboid64.exe", "ProjectZomboid64.exe"));
		Assert.True(PrismGameSignals.ShouldForgetLockedProcess("java.exe", "java -Xmx8g zombie.network.GameServer"));
		Assert.False(PrismGameSignals.ShouldForgetLockedProcess("", null));
		string dir = Path.Combine(Path.GetTempPath(), "norcom-lock-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "game.lock");
		using Process process = Process.GetCurrentProcess();
		StartupHeal.WriteGameLock(path, process.Id);
		StartupHeal.CommandReader = pid => pid == process.Id ? ("java.exe", "") : null;
		try
		{
			Assert.False(StartupHeal.ReleaseForeignClientLock(path));
			Assert.True(File.Exists(path));
			Assert.True(StartupHeal.GameLockAlive(path));
			Assert.False(process.HasExited);
		}
		finally
		{
			StartupHeal.CommandReader = null;
		}
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
