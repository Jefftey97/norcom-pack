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
	public void JavaCommandLineCountsMinecraftNorcomOrTheInstance()
	{
		Assert.True(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw -Dfoo=minecraft net.minecraft.client.main.Main", null));
		Assert.True(PrismGameSignals.CommandLineIsGame("java", "java -cp libs NORCOM", null));
		Assert.True(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw -Dinstance=C:\\Prism\\instances\\NORCOM", "instances\\NORCOM"));
		Assert.False(PrismGameSignals.CommandLineIsGame("javaw.exe", "javaw -version", null));
		Assert.False(PrismGameSignals.CommandLineIsGame("prismlauncher.exe", "prismlauncher --launch NORCOM", null));
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
