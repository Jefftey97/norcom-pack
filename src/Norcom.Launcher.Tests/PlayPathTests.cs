using System.Diagnostics;
using System.Net.Http;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PlayPathTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "norcom-play-" + Guid.NewGuid().ToString("n"));

	public PlayPathTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		StartupHeal.CommandReader = null;
		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
		}
	}

	[Fact]
	public async Task FreshInstallLaunchesAfterThePackSync()
	{
		RecordingProcess process = new RecordingProcess();
		List<string> steps = new List<string>();
		PlayOutcome outcome = await Launch(process, steps, existing: false, sync: () => steps.Add("sync"));
		Assert.True(outcome.Started);
		Assert.Equal(new[] { "sync", "settings", "prism" }, steps);
		Assert.Equal(new[] { "prism.exe" }, process.Started);
	}

	[Fact]
	public async Task ExistingInstallLaunchesAndKeepsGoingWhenTheFeedIsDown()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => throw new HttpRequestException("offline"));
		Assert.True(outcome.Started);
		Assert.Contains(PlayConductor.PackSkipped, outcome.Message, StringComparison.Ordinal);
		Assert.Equal(new[] { "prism.exe" }, process.Started);
	}

	[Fact]
	public async Task FreshInstallNamesTheFeedFailureAndDoesNotStart()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: false, sync: () => throw new HttpRequestException("offline"));
		Assert.False(outcome.Started);
		Assert.Contains("press Play again", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task PackUpdateRunsBeforeTheProcessStarts()
	{
		RecordingProcess process = new RecordingProcess();
		List<string> steps = new List<string>();
		PlayOutcome outcome = await Launch(process, steps, existing: true, sync: () => steps.Add("update"));
		Assert.True(outcome.Started);
		Assert.Equal("update", steps[0]);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task HealedPinStillLaunches()
	{
		RecordingProcess process = new RecordingProcess();
		bool healed = false;
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => healed = true);
		Assert.True(healed);
		Assert.True(outcome.Started);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task PinMismatchDoesNotLaunchAndNamesTheNextStep()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => throw new PinMismatchException(PinGate.BlockCopy));
		Assert.False(outcome.Started);
		Assert.Equal(PinGate.BlockCopy, outcome.Message);
		Assert.Contains("press Play again", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task MissingLockLaunches()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, lockPath: Path.Combine(_root, "missing.lock"));
		Assert.True(outcome.Started);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task DeadLockLaunchesAndDropsTheFile()
	{
		string path = Lock(int.MaxValue, 1);
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, lockPath: path);
		Assert.True(outcome.Started);
		Assert.False(File.Exists(path));
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task ServerJavaReusingTheRecordedPidLaunchesWithoutStoppingThatProcess()
	{
		Process self = Process.GetCurrentProcess();
		string path = Lock(self.Id, self.StartTime.ToUniversalTime().Ticks);
		StartupHeal.CommandReader = _ => ("java.exe", "G:\\NORCOM\\Server\\java\\java.exe -jar fabric-server-launch.jar nogui");
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, lockPath: path);
		Assert.True(outcome.Started);
		Assert.False(File.Exists(path));
		Assert.False(self.HasExited);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task LiveClientLockDoesNotLaunchASecondGame()
	{
		Process self = Process.GetCurrentProcess();
		string path = Lock(self.Id, self.StartTime.ToUniversalTime().Ticks);
		StartupHeal.CommandReader = _ => ("javaw.exe", "javaw net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir C:\\NORCOM\\minecraft");
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, lockPath: path);
		Assert.False(outcome.Started);
		Assert.Equal(PlayAdmission.AlreadyRunning, outcome.Message);
		Assert.True(File.Exists(path));
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task UnreadableCommandLineKeepsTheLockAndSaysWhatToDo()
	{
		Process self = Process.GetCurrentProcess();
		string path = Lock(self.Id, self.StartTime.ToUniversalTime().Ticks);
		StartupHeal.CommandReader = _ => ("java.exe", "");
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, lockPath: path);
		Assert.False(outcome.Started);
		Assert.Equal(PlayAdmission.UnreadableProcess, outcome.Message);
		Assert.Contains("press Play", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.True(File.Exists(path));
		Assert.Empty(process.Started);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task RamPendingDoesNotDecideWhetherPlayStarts(bool pending)
	{
		RecordingProcess process = new RecordingProcess();
		bool sawPending = false;
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, after: () => sawPending = pending);
		Assert.Equal(pending, sawPending);
		Assert.True(outcome.Started);
		Assert.Single(process.Started);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task SavedOrUnsavedTextureChoiceStillLaunches(bool saved)
	{
		RecordingProcess process = new RecordingProcess();
		string choice = "";
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, after: () => choice = saved ? TexturePackChoice.Patrix : "");
		Assert.Equal(saved ? TexturePackChoice.Patrix : "", choice);
		Assert.True(outcome.Started);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task UnsignedAccountAndServerStatusDoNotBlockPlay()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { });
		Assert.True(outcome.Started);
		Assert.DoesNotContain("sign in", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Single(process.Started);
	}

	[Fact]
	public async Task MissingPrismIsAMessageAndDoesNotStartAProcess()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, prism: _ => new PrismPreparation { CanStart = false, Message = PrismMessages.Missing });
		Assert.False(outcome.Started);
		Assert.Equal(PrismMessages.Missing, outcome.Message);
		Assert.Contains("press Play", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task CorruptPrismNamesTheFailure()
	{
		RecordingProcess process = new RecordingProcess { ExitCode = 1 };
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, useRealRun: true);
		Assert.False(outcome.Started);
		Assert.Equal(PrismMessages.Rejected, outcome.Message);
		Assert.Equal(new[] { "prism.exe" }, process.Started);
	}

	[Fact]
	public async Task JavaFailureIsAMessageAndDoesNotStart()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await Launch(process, new List<string>(), existing: true, sync: () => { }, java: () => throw new InvalidDataException("corrupt"));
		Assert.False(outcome.Started);
		Assert.Contains("Java 25", outcome.Message, StringComparison.Ordinal);
		Assert.Contains("press Play again", outcome.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task SecondClickWhileLaunchingDoesNotStartAnotherProcess()
	{
		RecordingProcess process = new RecordingProcess();
		PlayOutcome outcome = await PlayConductor.MinecraftAsync(new MinecraftPlayRequest { Busy = true, RunPrism = (_, _) => { process.Started.Add("prism.exe"); return Task.FromResult(new PrismPreparation { CanStart = true }); } }, CancellationToken.None);
		Assert.False(outcome.Started);
		Assert.Equal(PlayConductor.AlreadyStarting, outcome.Message);
		Assert.Empty(process.Started);
	}

	[Fact]
	public async Task PrismThatStaysOpenWithoutAGameStopsWaiting()
	{
		TaskCompletionSource<int> gate = new TaskCompletionSource<int>();
		PrismPreparation result = await PrismPlay.RunAsync(new PrismPreparation
		{
			CanStart = true,
			Executable = "prism.exe",
			Arguments = Array.Empty<string>()
		}, new HangProcess(gate.Task), CancellationToken.None, new SilentSignals(), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
		Assert.False(result.CanStart);
		Assert.Equal(PrismMessages.NotDetected, result.Message);
		Assert.Contains("press Play again", result.Message, StringComparison.OrdinalIgnoreCase);
		Assert.False(gate.Task.IsCompleted);
	}

	[Fact]
	public void EmptyPasswordStillOpensSteam()
	{
		string url = "";
		ZomboidLaunchPlan plan = new ZomboidLaunchPlan { CanStart = true, Url = "steam://rungameid/108600", Message = "Opening Project Zomboid in Steam." };
		PlayOutcome outcome = PlayConductor.Zomboid(new ZomboidPlayRequest { Plan = plan, Open = value => url = value });
		Assert.True(outcome.Started);
		Assert.Equal("steam://rungameid/108600", url);
		Assert.DoesNotContain("password", url, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void MissingSteamDoesNotOpenAUrl()
	{
		int opened = 0;
		PlayOutcome outcome = PlayConductor.Zomboid(new ZomboidPlayRequest
		{
			Plan = new ZomboidLaunchPlan { CanStart = false, Message = SteamLibrary.NotFound },
			Open = _ => opened++
		});
		Assert.False(outcome.Started);
		Assert.Equal(SteamLibrary.NotFound, outcome.Message);
		Assert.Equal(0, opened);
	}

	[Fact]
	public void ZomboidAlreadyRunningDoesNotOpenSteam()
	{
		int opened = 0;
		PlayOutcome outcome = PlayConductor.Zomboid(new ZomboidPlayRequest
		{
			AlreadyRunning = true,
			Plan = new ZomboidLaunchPlan { CanStart = true, Url = "steam://rungameid/108600" },
			Open = _ => opened++
		});
		Assert.False(outcome.Started);
		Assert.Equal(PlayConductor.ZomboidRunning, outcome.Message);
		Assert.Equal(0, opened);
	}

	[Fact]
	public void SecondZomboidClickDoesNotOpenSteam()
	{
		int opened = 0;
		PlayOutcome outcome = PlayConductor.Zomboid(new ZomboidPlayRequest { Busy = true, Open = _ => opened++ });
		Assert.False(outcome.Started);
		Assert.Equal(PlayConductor.AlreadyStarting, outcome.Message);
		Assert.Equal(0, opened);
	}

	[Fact]
	public void FeedDownAndExpiredFeedStillOpenTheSavedGame()
	{
		string none = PlayFeeds.Choose("", "", null, DateTimeOffset.UtcNow, out string down);
		Assert.Equal("", none);
		Assert.Equal(PlayFeeds.FeedDown, down);
		string expired = PlayFeeds.Choose("", "{\"workshop\":[]}", DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow, out string old);
		Assert.Contains("workshop", expired, StringComparison.Ordinal);
		Assert.Equal(PlayFeeds.ExpiredCache, old);
		string url = "";
		PlayOutcome outcome = PlayConductor.Zomboid(new ZomboidPlayRequest
		{
			Plan = new ZomboidLaunchPlan { CanStart = true, Url = "steam://rungameid/108600", Message = "Opening Project Zomboid in Steam." },
			Open = value => url = value,
			FeedNote = old
		});
		Assert.True(outcome.Started);
		Assert.Contains(PlayFeeds.ExpiredCache, outcome.Message, StringComparison.Ordinal);
		Assert.Equal("steam://rungameid/108600", url);
	}

	[Fact]
	public void OnlineAndOfflineServerStatusAreNotPlayInputs()
	{
		Assert.True(PlayInstall.HasLocalClient(WriteClient()));
		Assert.False(PlayInstall.HasLocalClient(Path.Combine(_root, "empty")));
	}

	private async Task<PlayOutcome> Launch(RecordingProcess process, List<string> steps, bool existing, Action sync, string lockPath = "", Action after = null, Func<string, PrismPreparation> prism = null, Func<string> java = null, bool useRealRun = false)
	{
		string game = existing ? WriteClient() : Path.Combine(_root, "fresh");
		if (!existing)
		{
			Directory.CreateDirectory(game);
		}
		return await PlayConductor.MinecraftAsync(new MinecraftPlayRequest
		{
			LockPath = lockPath,
			ExistingInstall = PlayInstall.HasLocalClient(game),
			EnsureJava = _ => Task.FromResult(java == null ? "java" : java()),
			SyncPack = _ =>
			{
				sync();
				return Task.CompletedTask;
			},
			AfterSync = () =>
			{
				steps.Add("settings");
				after?.Invoke();
			},
			PreparePrism = path =>
			{
				steps.Add("prism");
				return prism == null ? new PrismPreparation { CanStart = true, Executable = "prism.exe", Arguments = Array.Empty<string>() } : prism(path);
			},
			RunPrism = (prepared, token) =>
			{
				if (!useRealRun)
				{
					process.Started.Add(prepared.Executable ?? "");
					return Task.FromResult(new PrismPreparation { CanStart = true, Message = PrismMessages.Closed, Executable = prepared.Executable });
				}
				return PrismPlay.RunAsync(prepared, process, token, new SilentSignals { Running = process.ExitCode == 0 }, TimeSpan.Zero, TimeSpan.Zero);
			},
			ClientRunning = () => process.ExitCode == 0 && process.Started.Count > 0 && useRealRun
		}, CancellationToken.None);
	}

	private string WriteClient()
	{
		string dir = Path.Combine(_root, "game");
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "options.txt"), "renderDistance:12\nkey_key.attack:key.mouse.left\n");
		return dir;
	}

	private string Lock(int pid, long ticks)
	{
		string path = Path.Combine(_root, "game.lock");
		File.WriteAllText(path, "pid=" + pid + "\nprocessStartedUtcTicks=" + ticks + "\n");
		return path;
	}

	private sealed class RecordingProcess : IPrismProcess
	{
		public int ExitCode { get; set; }

		public List<string> Started { get; } = new List<string>();

		public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
		{
			Started.Add(executable);
			return Task.FromResult(ExitCode);
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

	private sealed class SilentSignals : IPrismGameSignals
	{
		public bool Running { get; set; }

		public bool MinecraftProcessRunning()
		{
			return Running;
		}

		public bool LogFreshenedSinceLaunch()
		{
			return false;
		}
	}
}
