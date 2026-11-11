using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PrismPlayTests
{
	[Fact]
	public async Task ExitZeroWithoutAGameIsNotClosed()
	{
		PrismPreparation result = await Run(0, new FakeSignals());
		Assert.False(result.CanStart);
		Assert.Equal(PrismMessages.ClosedWithoutGame, result.Message);
		Assert.Contains("without Minecraft", result.Message);
		Assert.Contains("Microsoft", result.Message);
		Assert.Contains("press Play again", result.Message);
		Assert.DoesNotContain("Later Play uses the same Prism", result.Message);
	}

	[Fact]
	public async Task ExitZeroWithJavawKeepsClosed()
	{
		PrismPreparation result = await Run(0, new FakeSignals { Running = true });
		Assert.True(result.CanStart);
		Assert.Equal(PrismMessages.Closed, result.Message);
	}

	[Fact]
	public async Task ExitZeroWithOnlyAFreshLogIsNotAStart()
	{
		PrismPreparation result = await Run(0, new FakeSignals { Freshened = true });
		Assert.False(result.CanStart);
		Assert.Equal(PrismMessages.ClosedWithoutGame, result.Message);
	}

	[Fact]
	public async Task MinecraftStartingWhilePrismStaysOpenFinishesPlay()
	{
		TaskCompletionSource<int> gate = new TaskCompletionSource<int>();
		PrismPreparation result = await PrismPlay.RunAsync(new PrismPreparation
		{
			CanStart = true,
			Executable = "prismlauncher.exe",
			Message = PrismMessages.Opening
		}, new HangProcess(gate.Task), CancellationToken.None, new FakeSignals { Running = true }, TimeSpan.FromSeconds(15));
		Assert.True(result.CanStart);
		Assert.Equal(PrismMessages.Closed, result.Message);
		Assert.False(gate.Task.IsCompleted);
	}

	[Fact]
	public async Task NonZeroExitDoesNotProbeForTheGame()
	{
		FakeSignals signals = new FakeSignals { Running = true, Freshened = true };
		PrismPreparation result = await Run(1, signals);
		Assert.False(result.CanStart);
		Assert.Equal(PrismMessages.Rejected, result.Message);
		Assert.Equal(0, signals.ProcessPolls);
		Assert.Equal(0, signals.LogPolls);
	}

	[Fact]
	public async Task BusyDownloadHoldsTheSamePrismUntilItIsIdle()
	{
		TaskCompletionSource<int> gate = new TaskCompletionSource<int>();
		CountingHang process = new CountingHang(gate.Task);
		DownloadWork signals = new DownloadWork { BusyPolls = 4 };
		PrismPreparation result = await PrismPlay.RunAsync(Ready(), process, CancellationToken.None, signals, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(150));
		Assert.False(result.CanStart);
		Assert.Equal(PrismMessages.NotDetected, result.Message);
		Assert.Equal(1, process.Starts);
		Assert.True(signals.Polls > 4);
		Assert.False(gate.Task.IsCompleted);
		signals.Hold = true;
		signals.BusyPolls = 0;
		signals.Polls = 0;
		CountingHang second = new CountingHang(gate.Task);
		PrismPreparation again = await PrismPlay.RunAsync(Ready(), second, CancellationToken.None, signals, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
		Assert.Equal(0, second.Starts);
		Assert.Equal(PrismMessages.NotDetected, again.Message);
		Assert.False(gate.Task.IsCompleted);
	}

	[Fact]
	public void ProbeWaitsFifteenSeconds()
	{
		Assert.Equal(TimeSpan.FromSeconds(15), PrismPlay.ProbeBudget);
		Assert.Equal(TimeSpan.FromSeconds(90), PrismPlay.OpenBudget);
	}

	[Fact]
	public async Task AlreadyRunningPrismHandoffCountsThatPrismsClient()
	{
		int? seen = null;
		List<string> failures = new List<string>();
		Action<string> previous = PrismPlay.PlayFailed;
		PrismPlay.GameProcessObserved = pid => seen = pid;
		PrismPlay.PlayFailed = message => failures.Add(message);
		try
		{
			Assert.Equal(40252, PrismGameSignals.SelectGamePid(HandoffRows(), InstanceHint, 99999, GameDirHint));
			PrismPreparation result = await PrismPlay.RunAsync(Ready(), new TrackedExit(0) { ProcessId = 99999 }, CancellationToken.None, new RowSignals(HandoffRows()), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
			Assert.True(result.CanStart);
			Assert.Equal(PrismMessages.Closed, result.Message);
			Assert.Equal(40252, seen);
			Assert.Empty(failures);
		}
		finally
		{
			PrismPlay.GameProcessObserved = null;
			PrismPlay.PlayFailed = previous;
		}
	}

	[Fact]
	public async Task PrismThatIsNotRunningStillCountsTheChildItStarted()
	{
		GameProcessRow child = new GameProcessRow("java.exe", "", 7001, 5100, GameDirHint);
		int? seen = null;
		List<string> failures = new List<string>();
		Action<string> previous = PrismPlay.PlayFailed;
		PrismPlay.GameProcessObserved = pid => seen = pid;
		PrismPlay.PlayFailed = message => failures.Add(message);
		try
		{
			DelayedRows signals = new DelayedRows(new[] { ServerRow(), child }, 2);
			PrismPreparation result = await PrismPlay.RunAsync(Ready(), new TrackedExit(0) { ProcessId = 5100 }, CancellationToken.None, signals, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
			Assert.True(result.CanStart);
			Assert.Equal(PrismMessages.Closed, result.Message);
			Assert.Equal(7001, seen);
			Assert.Empty(failures);
		}
		finally
		{
			PrismPlay.GameProcessObserved = null;
			PrismPlay.PlayFailed = previous;
		}
	}

	[Fact]
	public void FirstLaunchCountsANestedNorcomClientAndNotServerJava()
	{
		GameProcessRow server = ServerRow() with { ParentPid = 5100 };
		GameProcessRow wrapper = new GameProcessRow("java.exe", "", 6000, 5100, "");
		GameProcessRow client = new GameProcessRow("javaw.exe", "javaw net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir " + GameDirHint, 7002, 6000, "");
		Assert.Equal(7002, PrismGameSignals.SelectGamePid(new[] { server, wrapper, client }, InstanceHint, 5100, GameDirHint));
		Assert.Equal(6000, PrismGameSignals.SelectGamePid(new[] { server, wrapper }, InstanceHint, 5100, GameDirHint));
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { server }, InstanceHint, 5100, GameDirHint));
	}

	[Fact]
	public async Task PrismThatStaysOpenCountsTheNorcomChild()
	{
		TaskCompletionSource<int> gate = new TaskCompletionSource<int>();
		GameProcessRow child = new GameProcessRow("javaw.exe", "javaw net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir " + GameDirHint, 7002, 5100, "");
		int? seen = null;
		PrismPlay.GameProcessObserved = pid => seen = pid;
		try
		{
			PrismPreparation result = await PrismPlay.RunAsync(Ready(), new TrackedHang(gate.Task) { ProcessId = 5100 }, CancellationToken.None, new RowSignals(new[] { ServerRow(), child }), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
			Assert.True(result.CanStart);
			Assert.Equal(7002, seen);
			Assert.False(gate.Task.IsCompleted);
		}
		finally
		{
			PrismPlay.GameProcessObserved = null;
		}
	}

	[Fact]
	public async Task SlowJavaStartShowsStartingUntilTheClientAppears()
	{
		List<string> statuses = new List<string>();
		List<string> failures = new List<string>();
		Action<string> previous = PrismPlay.PlayFailed;
		PrismPlay.StatusObserved = text => statuses.Add(text);
		PrismPlay.PlayFailed = message => failures.Add(message);
		try
		{
			DelayedRows signals = new DelayedRows(HandoffRows(), 2);
			PrismPreparation result = await PrismPlay.RunAsync(Ready(), new FakeProcess(0), CancellationToken.None, signals, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
			Assert.True(result.CanStart);
			Assert.Equal(PrismMessages.Closed, result.Message);
			Assert.Contains(PrismMessages.Starting, statuses);
			Assert.Empty(failures);
			Assert.True(signals.Polls >= 3);
		}
		finally
		{
			PrismPlay.StatusObserved = null;
			PrismPlay.PlayFailed = previous;
		}
	}

	[Fact]
	public async Task MissingClientAfterPrismExitsIsAFailure()
	{
		List<string> failures = new List<string>();
		List<string> statuses = new List<string>();
		Action<string> previous = PrismPlay.PlayFailed;
		PrismPlay.PlayFailed = message => failures.Add(message);
		PrismPlay.StatusObserved = text => statuses.Add(text);
		try
		{
			PrismPreparation result = await PrismPlay.RunAsync(Ready(), new FakeProcess(0), CancellationToken.None, new RowSignals(new[] { PrismRow(), ServerRow() }), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300));
			Assert.False(result.CanStart);
			Assert.Equal(PrismMessages.ClosedWithoutGame, result.Message);
			Assert.Equal(new[] { PrismMessages.ClosedWithoutGame }, failures);
			Assert.Contains(PrismMessages.Starting, statuses);
		}
		finally
		{
			PrismPlay.PlayFailed = previous;
			PrismPlay.StatusObserved = null;
		}
	}

	[Fact]
	public void ServerJavaIsNeverTheLaunchedClient()
	{
		GameProcessRow server = ServerRow();
		server = server with { ParentPid = 46764, WorkingDirectory = @"G:\NORCOM\Server" };
		Assert.Null(PrismGameSignals.SelectGamePid(new[] { PrismRow(), server }, InstanceHint, 99999, GameDirHint));
		Assert.Equal(40252, PrismGameSignals.SelectGamePid(HandoffRows(), InstanceHint, 99999, GameDirHint));
		Assert.True(PrismGameSignals.IsServerJava("java.exe", @"G:\NORCOM\Server\java\java.exe", @"G:\NORCOM\Server"));
		Assert.False(PrismGameSignals.IsServerJava("java.exe", "", GameDirHint));
	}

	[Fact]
	public async Task ObservedGamePublishesThePrismChildPid()
	{
		int? seen = null;
		PrismPlay.GameProcessObserved = pid => seen = pid;
		try
		{
			TaskCompletionSource<int> gate = new TaskCompletionSource<int>();
			PidSignals signals = new PidSignals { Pid = 4242 };
			TrackedHang process = new TrackedHang(gate.Task) { ProcessId = 77 };
			PrismPreparation result = await PrismPlay.RunAsync(new PrismPreparation
			{
				CanStart = true,
				Executable = "prismlauncher.exe",
				Message = PrismMessages.Opening
			}, process, CancellationToken.None, signals, TimeSpan.FromSeconds(15));
			Assert.True(result.CanStart);
			Assert.Equal(PrismMessages.Closed, result.Message);
			Assert.Equal(4242, seen);
			Assert.Equal(77, signals.SeenPrism);
			Assert.False(gate.Task.IsCompleted);
		}
		finally
		{
			PrismPlay.GameProcessObserved = null;
		}
	}

	[Fact]
	public void JavaCommandLineCountsOnlyTheClientWeLaunch()
	{
		const string server = @"G:\NORCOM\Server\java\jdk-25.0.5+8\bin\java.exe -Xms8G -Xmx8G -jar G:\NORCOM\Server\fabric-server-launch.jar nogui";
		const string client = @"C:\Prism\java\javaw.exe -Xmx6G -Djava.library.path=natives net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir C:\Prism\instances\NORCOM\minecraft";
		Assert.False(PrismGameSignals.CommandLineIsGame("java.exe", server, @"C:\Prism\instances\NORCOM", @"C:\NORCOM\minecraft"));
		Assert.False(PrismGameSignals.CommandLineIsGame("java.exe", @"G:\NORCOM\Server\java\jdk-25.0.5+8\bin\java.exe -Xms8G -Xmx8G", @"C:\Prism\instances\NORCOM"));
		Assert.False(PrismGameSignals.CommandLineIsGame("java", "java -cp libs NORCOM", null));
		Assert.False(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw -Dfoo=minecraft net.minecraft.client.main.Main", null));
		Assert.True(PrismGameSignals.CommandLineIsGame("javaw.exe", client, @"C:\Prism\instances\NORCOM"));
		Assert.True(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw --gameDir C:\\NORCOM\\minecraft net.minecraft.client.main.Main", null, @"C:\NORCOM\minecraft"));
		Assert.False(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw -version", null));
		Assert.False(PrismGameSignals.CommandLineIsGame("prismlauncher.exe", "prismlauncher --launch NORCOM", null));
		Assert.False(PrismGameSignals.IsZomboidClient("java.exe", server));
		Assert.False(PrismGameSignals.IsZomboidClient("java.exe", "java -Xms8G -Xmx8G zombie.network.GameServer"));
		Assert.False(PrismGameSignals.IsZomboidClient("ProjectZomboidServer64.exe", ""));
		Assert.True(PrismGameSignals.IsZomboidClient("ProjectZomboid64.exe", ""));
		Assert.False(PrismGameSignals.IsZomboidClient("ProjectZomboid64.exe", "ProjectZomboid64.exe StartServer64"));
	}

	[Fact]
	public void LogMtimeCountsOnlyWhenItMovesAfterLaunch()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-log-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string log = Path.Combine(dir, "latest.log");
		File.WriteAllText(log, "old");
		DateTime baseline = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(log, baseline);
		Assert.False(PrismGameSignals.MtimeFreshened(baseline, log));
		File.SetLastWriteTimeUtc(log, baseline.AddMinutes(1));
		Assert.True(PrismGameSignals.MtimeFreshened(baseline, log));
		string created = Path.Combine(dir, "missing.log");
		Assert.False(PrismGameSignals.MtimeFreshened(null, created));
		File.WriteAllText(created, "appeared");
		Assert.True(PrismGameSignals.MtimeFreshened(null, created));
	}

	private const string InstanceHint = @"C:\Prism\instances\NORCOM";

	private const string GameDirHint = @"C:\Prism\instances\NORCOM\minecraft";

	private static PrismPreparation Ready()
	{
		return new PrismPreparation
		{
			CanStart = true,
			Executable = "prismlauncher.exe",
			Message = PrismMessages.Opening
		};
	}

	private static GameProcessRow PrismRow()
	{
		return new GameProcessRow("prismlauncher.exe", "prismlauncher.exe", 46764, 1000);
	}

	private static GameProcessRow ServerRow()
	{
		return new GameProcessRow("java.exe", @"G:\NORCOM\Server\java\jdk-25\bin\java.exe -Xmx8G -jar G:\NORCOM\Server\fabric-server-launch.jar nogui", 30920, 4, @"G:\NORCOM\Server");
	}

	private static GameProcessRow[] HandoffRows()
	{
		return new GameProcessRow[3]
		{
			PrismRow(),
			ServerRow(),
			new GameProcessRow("java.exe", "", 40252, 46764, GameDirHint)
		};
	}

	private static Task<PrismPreparation> Run(int exitCode, IPrismGameSignals signals)
	{
		return PrismPlay.RunAsync(new PrismPreparation
		{
			CanStart = true,
			Executable = "prismlauncher.exe",
			Message = PrismMessages.Opening
		}, new FakeProcess(exitCode), CancellationToken.None, signals, TimeSpan.Zero);
	}

	private sealed class TrackedHang : ITrackedPrismProcess
	{
		private readonly Task<int> _task;

		public TrackedHang(Task<int> task)
		{
			_task = task;
		}

		public int? ProcessId { get; set; }

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			return _task;
		}
	}

	private sealed class PidSignals : IPrismGameSignals, IGameProcessLookup
	{
		public int? Pid { get; set; }

		public int? SeenPrism { get; private set; }

		public bool MinecraftProcessRunning()
		{
			return false;
		}

		public bool LogFreshenedSinceLaunch()
		{
			return false;
		}

		public int? TryGameProcessId(int? prismProcessId)
		{
			SeenPrism = prismProcessId;
			return Pid;
		}
	}

	private sealed class CountingHang : IPrismProcess
	{
		private readonly Task<int> _task;

		public CountingHang(Task<int> task)
		{
			_task = task;
		}

		public int Starts { get; private set; }

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			Starts++;
			return _task;
		}
	}

	private sealed class DownloadWork : IPrismGameSignals, IPrismWork
	{
		public int BusyPolls { get; set; }

		public int Polls { get; set; }

		public bool Hold { get; set; }

		public bool MinecraftProcessRunning()
		{
			return false;
		}

		public bool LogFreshenedSinceLaunch()
		{
			return false;
		}

		public bool LauncherAlreadyRunning()
		{
			return Hold;
		}

		public bool DownloadMoving()
		{
			Polls++;
			return Polls <= BusyPolls;
		}
	}

	private sealed class HangProcess : IPrismProcess
	{
		private readonly Task<int> _task;

		public HangProcess(Task<int> task)
		{
			_task = task;
		}

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			return _task;
		}
	}

	private sealed class TrackedExit : ITrackedPrismProcess
	{
		private readonly int _code;

		public TrackedExit(int code)
		{
			_code = code;
		}

		public int? ProcessId { get; set; }

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			return Task.FromResult(_code);
		}
	}

	private sealed class RowSignals : IPrismGameSignals, IGameProcessLookup
	{
		private readonly GameProcessRow[] _rows;

		public RowSignals(GameProcessRow[] rows)
		{
			_rows = rows;
		}

		public int Polls { get; private set; }

		public bool MinecraftProcessRunning()
		{
			return TryGameProcessId(null) != null;
		}

		public bool LogFreshenedSinceLaunch()
		{
			return false;
		}

		public int? TryGameProcessId(int? prismProcessId)
		{
			Polls++;
			return PrismGameSignals.SelectGamePid(_rows, InstanceHint, prismProcessId, GameDirHint);
		}
	}

	private sealed class DelayedRows : IPrismGameSignals, IGameProcessLookup
	{
		private readonly GameProcessRow[] _rows;

		private readonly int _readyAfter;

		public DelayedRows(GameProcessRow[] rows, int readyAfter)
		{
			_rows = rows;
			_readyAfter = readyAfter;
		}

		public int Polls { get; private set; }

		public bool MinecraftProcessRunning()
		{
			return false;
		}

		public bool LogFreshenedSinceLaunch()
		{
			return false;
		}

		public int? TryGameProcessId(int? prismProcessId)
		{
			Polls++;
			if (Polls <= _readyAfter)
			{
				return null;
			}
			return PrismGameSignals.SelectGamePid(_rows, InstanceHint, prismProcessId, GameDirHint);
		}
	}

	private sealed class FakeProcess : IPrismProcess
	{
		private readonly int _code;

		public FakeProcess(int code)
		{
			_code = code;
		}

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			return Task.FromResult(_code);
		}
	}

	private sealed class FakeSignals : IPrismGameSignals
	{
		public bool Running { get; set; }

		public bool Freshened { get; set; }

		public int ProcessPolls { get; private set; }

		public int LogPolls { get; private set; }

		public bool MinecraftProcessRunning()
		{
			ProcessPolls++;
			return Running;
		}

		public bool LogFreshenedSinceLaunch()
		{
			LogPolls++;
			return Freshened;
		}
	}
}
