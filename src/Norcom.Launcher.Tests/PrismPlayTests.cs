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
	public async Task ExitZeroWithAFreshLogKeepsClosed()
	{
		PrismPreparation result = await Run(0, new FakeSignals { Freshened = true });
		Assert.True(result.CanStart);
		Assert.Equal(PrismMessages.Closed, result.Message);
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
	public void ProbeWaitsFifteenSeconds()
	{
		Assert.Equal(TimeSpan.FromSeconds(15), PrismPlay.ProbeBudget);
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
