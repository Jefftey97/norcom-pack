using System.Collections.Generic;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games;

public interface IGame
{
	string Id { get; }

	string Title { get; }

	bool PasswordEnabled { get; }

	bool ChecksPackOnPlay { get; }

	string PlayLabel(bool running);

	string Availability(bool offline, bool busy, bool running);

	bool CanPlay(bool offline, bool busy, bool running);

	bool CanStop(string lockPath);

	string SessionNote { get; }

	string PasswordPrompt { get; }

	string AccountTitle { get; }

	string AccountBody { get; }

	string AccountFine { get; }

	string AccountAction { get; }

	string ClientPackDetail(string pinDetail);

	string PickerDetail { get; }

	string PackCheckNote { get; }

	int StoredRamMb();

	RamBudget DisplayBudget(long totalBytes, long freeBytes);

	void CommitRam(int megabytes, long totalBytes, long freeBytes);

	void SetPassword(string password);

	string TextureLabel();

	string TextureHint();

	void ChooseTexture(string choice);

	string AccountUrl { get; }

	string GameFolder();

	string ScreenshotsFolder();

	Task PlayAsync(IPlayContext context);

	IGameBoard Board { get; }
}

public interface IGameBoard
{
	string Id { get; }

	string Heading { get; }

	bool ShowTexturePacks { get; }

	GameDefinition Definition { get; }

	string ConfigDirectory(string gameDir, string userProfile);

	IReadOnlyList<DiscoveredModFile> DiscoverMods(string configDirectory, string schema);

	string Apply(SavedGraphicsProfile profile, string gameDir, string userProfile);

	void RefreshFeed(System.Action rebuild);
}

public interface IPlayContext
{
	System.Net.Http.HttpClient Http { get; }

	bool IsBusy { get; }

	bool HandoffScheduled { get; }

	void SetBusy(bool value);

	double ProgressFraction { get; set; }

	string ProgressLabel { get; set; }

	string ProgressDetail { get; set; }

	string StatusLine { get; set; }

	string AccountStatus { get; set; }

	bool LauncherSounds { get; }

	void PlaySound();

	int MemoryMb { get; }

	bool GameRunning { get; }

	void SetGameRunning(bool value);

	System.Threading.CancellationTokenSource StartMeter(TransferMeter meter);

	void StopMeter(System.Threading.CancellationTokenSource pump);

	Task<bool> AcquirePin(System.TimeSpan timeout);

	void ReleasePin(bool owned);

	Task PollPin(System.Threading.CancellationToken ct);

	Task LoadNews();

	Task LoadDiscord();

	void ArmWatch();

	void AttachObservers();

	void DetachObservers();

	void RefreshLaunched();

	void Apply(PlayOutcome outcome);

	void NotifyCommands();

	string AvailabilityText();
}
