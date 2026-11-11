using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Games;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Games.Zomboid;

public sealed class ZomboidGame : IGame
{
	private ZomboidState _state;

	private string _password = "";

	private bool _passwordTouched;

	public string Id => ZomboidGraphics.Id;

	public string Title => "Project Zomboid";

	public bool PasswordEnabled => true;

	public bool ChecksPackOnPlay => false;

	public string SessionNote => "Play syncs the server's mod list from the pack, then connects in Steam. The first time, subscribe to any Workshop mods NORCOM names. You only need to own Project Zomboid.";

	public string PasswordPrompt => "Server password, saved on this PC only. Leave blank if the server has none.";

	public string AccountTitle => "Owned on Steam";

	public string AccountBody => "NORCOM opens Project Zomboid through Steam. Sign in to Steam on this PC. NORCOM does not read your Steam password.";

	public string AccountFine => "Workshop mods are subscribed in Steam.";

	public string AccountAction => "Open Steam";

	public string PickerDetail
	{
		get
		{
			SteamGameLocation install = SteamLibrary.FindZomboid(SteamSources.Detect());
			return install.Found ? "Play checks the pack and opens Project Zomboid in Steam." : SteamLibrary.NotFound;
		}
	}

	public string AccountUrl => "steam://open/main";

	public string PackCheckNote => "Project Zomboid files are checked when you press Play.";

	public IGameBoard Board { get; } = new ZomboidBoard();

	public string PlayLabel(bool running)
	{
		_ = running;
		return "Play Project Zomboid";
	}

	public string Availability(bool offline, bool busy, bool running)
	{
		_ = running;
		if (offline)
		{
			return "Play is disabled for this offline verification session.";
		}
		if (busy)
		{
			return "Please wait for the current operation to finish.";
		}
		return PickerDetail;
	}

	public bool CanPlay(bool offline, bool busy, bool running)
	{
		_ = running;
		return !offline && !busy;
	}

	public bool CanStop(string lockPath)
	{
		return ZomboidSession.Running(lockPath);
	}

	public string ClientPackDetail(string pinDetail)
	{
		_ = pinDetail;
		return PackCheckNote;
	}

	public int StoredRamMb()
	{
		ZomboidState state = State();
		return state.RamSpecified ? state.RamMb : 0;
	}

	public RamBudget DisplayBudget(long totalBytes, long freeBytes)
	{
		string json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", ZomboidMemory.FileName);
		return DisplayBudget(totalBytes, freeBytes, json);
	}

	public RamBudget DisplayBudget(long totalBytes, long freeBytes, string jsonPath)
	{
		ZomboidState state = State();
		if (state.RamSpecified)
		{
			return RamBudget.Create(totalBytes, freeBytes, state.RamMb);
		}
		return RamBudget.ForUnchangedSlider(totalBytes, freeBytes, ZomboidMemory.ReadCurrentMb(jsonPath));
	}

	public void CommitRam(int megabytes, long totalBytes, long freeBytes)
	{
		RamBudget budget = RamBudget.Create(totalBytes, freeBytes, megabytes);
		ZomboidState state = State();
		state.RamMb = budget.ChosenMb;
		state.RamSpecified = true;
		state.RamPending = true;
		state.Save(NorcomPaths.NorcomHome);
	}

	public string TexturePackName()
	{
		return "";
	}

	public string TextureLabel()
	{
		return "";
	}

	public string TextureHint()
	{
		return "";
	}

	public void ChooseTexture(string choice)
	{
		_ = choice;
	}

	public string GameFolder()
	{
		SteamGameLocation install = SteamLibrary.FindZomboid(SteamSources.Detect());
		return install.Found ? install.GamePath : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid");
	}

	public string ScreenshotsFolder()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "Screenshots");
	}

	public void SetPassword(string password)
	{
		_password = password ?? "";
		_passwordTouched = true;
	}

	public void OpenSteam()
	{
		SteamStart.Open("steam://open/main");
	}

	public async Task PlayAsync(IPlayContext context)
	{
		if (context.IsBusy || context.HandoffScheduled)
		{
			context.StatusLine = PlayConductor.AlreadyStarting;
			return;
		}
		context.SetBusy(true);
		try
		{
			if (ZomboidSession.Running(NorcomPaths.GameLockPath))
			{
				context.Apply(PlayConductor.Zomboid(new ZomboidPlayRequest { AlreadyRunning = true }));
				return;
			}
			context.ProgressFraction = 0.2;
			context.ProgressLabel = "Checking Steam";
			string live = "";
			using (CancellationTokenSource feed = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
			{
				try
				{
					live = await context.Http.GetStringAsync(ZomboidFeed.Url, feed.Token).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("zomboid feed " + ex.GetType().Name);
				}
			}
			string cachePath = ZomboidFeed.CachePath(NorcomPaths.NorcomHome);
			if (!string.IsNullOrWhiteSpace(live))
			{
				try
				{
					File.WriteAllText(cachePath, live);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("zomboid feed cache " + ex.GetType().Name);
				}
			}
			string cached = "";
			DateTimeOffset? cachedUtc = null;
			try
			{
				if (File.Exists(cachePath))
				{
					cached = File.ReadAllText(cachePath);
					cachedUtc = File.GetLastWriteTimeUtc(cachePath);
				}
			}
			catch (Exception ex)
			{
				LauncherLog.Write("zomboid feed read " + ex.GetType().Name);
			}
			string chosen = PlayFeeds.Choose(live, cached, cachedUtc, DateTimeOffset.UtcNow, out string feedNote);
			context.ProgressLabel = "Syncing Project Zomboid";
			ZomboidState state = State();
			int ram = state.RamPending ? context.MemoryMb : 0;
			ZomboidLaunchPlan plan;
			using (CancellationTokenSource prepare = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
			{
				plan = await ZomboidLaunch.PrepareAsync(SteamSources.Detect(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), NorcomPaths.NorcomHome, NorcomPaths.GameLockPath, chosen, context.Http, prepare.Token, Password(), ram).ConfigureAwait(false);
			}
			if (plan.Applied && state.RamPending)
			{
				state.RamPending = false;
				state.Save(NorcomPaths.NorcomHome);
			}
			if (!string.IsNullOrWhiteSpace(plan.GraphicsNote))
			{
				LauncherLog.Write(plan.GraphicsNote);
			}
			context.Apply(PlayConductor.Zomboid(new ZomboidPlayRequest
			{
				Plan = plan,
				Open = SteamStart.Open,
				FeedNote = feedNote
			}));
		}
		catch (Exception ex)
		{
			context.ProgressFraction = 0.0;
			context.ProgressLabel = "Play didn't finish";
			context.StatusLine = Redactor.Clean(ex.Message);
			LauncherLog.Write("zomboid play " + ex.GetType().Name);
		}
		finally
		{
			context.SetBusy(context.HandoffScheduled);
			context.NotifyCommands();
		}
	}

	private string Password()
	{
		if (!_passwordTouched && ZomboidPasswordStore.Exists(NorcomPaths.NorcomHome))
		{
			return ZomboidPasswordStore.Load(NorcomPaths.NorcomHome);
		}
		ZomboidPasswordStore.Save(NorcomPaths.NorcomHome, _password);
		return _password;
	}

	private ZomboidState State()
	{
		return _state ??= ZomboidState.Load(NorcomPaths.NorcomHome);
	}

	private sealed class ZomboidBoard : IGameBoard
	{
		public string Id => ZomboidGraphics.Id;

		public string Heading => "Project Zomboid";

		public bool ShowTexturePacks => false;

		public GameDefinition Definition => ZomboidGraphics.Definition;

		public string ConfigDirectory(string gameDir, string userProfile)
		{
			_ = gameDir;
			return Path.Combine(userProfile, "Zomboid", "mods");
		}

		public IReadOnlyList<DiscoveredModFile> DiscoverMods(string configDirectory, string schema)
		{
			string cached = ZomboidFeed.CachePath(NorcomPaths.NorcomHome);
			string feedJson = File.Exists(cached) ? File.ReadAllText(cached) : "";
			ZomboidFeedDoc feed = new ZomboidFeedDoc();
			try
			{
				feed = ZomboidFeed.Parse(feedJson);
			}
			catch (JsonException)
			{
				feed = new ZomboidFeedDoc();
			}
			return ModSettingsCatalog.DiscoverZomboidFeed(configDirectory, feed, schema);
		}

		public string Apply(SavedGraphicsProfile profile, string gameDir, string userProfile)
		{
			return ZomboidSettings.Apply(profile, gameDir, userProfile);
		}

		public void RefreshFeed(Action rebuild)
		{
			if (QaGates.OfflineCheckRequested())
			{
				return;
			}
			_ = Refresh(rebuild);
		}

		private static async Task Refresh(Action rebuild)
		{
			try
			{
				using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
				string json = await http.GetStringAsync(ZomboidFeed.Url).ConfigureAwait(true);
				Directory.CreateDirectory(NorcomPaths.NorcomHome);
				File.WriteAllText(ZomboidFeed.CachePath(NorcomPaths.NorcomHome), json);
				rebuild();
			}
			catch (Exception ex)
			{
				LauncherLog.Write("zomboid mods " + ex.GetType().Name);
			}
		}
	}
}
