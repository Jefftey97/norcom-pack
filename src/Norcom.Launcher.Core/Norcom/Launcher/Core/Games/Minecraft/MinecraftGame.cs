using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Games;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Games.Minecraft;

public sealed class MinecraftGame : IGame
{
	private MinecraftState _state;

	public string Id => MinecraftGraphics.Id;

	public string Title => "Minecraft";

	public bool PasswordEnabled => false;

	public bool ChecksPackOnPlay => true;

	public string SessionNote => "Play checks the client and opens your NORCOM instance in Prism.";

	public string PasswordPrompt => "Server password is not used for this game.";

	public string AccountTitle => "Managed in Prism";

	public string AccountBody => "Sign in with Microsoft in Prism when prompted. NORCOM opens your instance there and does not read Prism’s accounts.";

	public string AccountFine => "Prism Launcher is a separate GPL program.";

	public string AccountAction => "Get Prism Launcher";

	public string PickerDetail => "This game uses the NORCOM pack in Prism.";

	public string PackCheckNote => "";

	public string AccountUrl => "https://prismlauncher.org/download/";

	public IGameBoard Board { get; } = new MinecraftBoard();

	public string PlayLabel(bool running) => LaunchedGame.PlayCaption(running);

	public string Availability(bool offline, bool busy, bool running)
	{
		if (offline)
		{
			return "Play is disabled for this offline verification session.";
		}
		if (running)
		{
			return "Minecraft is already running.";
		}
		if (busy)
		{
			return "Please wait for the current operation to finish.";
		}
		return "Microsoft sign-in is handled in Prism when needed.";
	}

	public bool CanPlay(bool offline, bool busy, bool running)
	{
		return !offline && !busy && !running;
	}

	public bool CanStop(string lockPath)
	{
		return MinecraftRunGate.Running(lockPath);
	}

	public string ClientPackDetail(string pinDetail)
	{
		return pinDetail ?? "";
	}

	public int StoredRamMb()
	{
		MinecraftState state = State();
		return state.RamSpecified ? state.RamMb : 0;
	}

	public void CommitRam(int megabytes, long totalBytes, long freeBytes)
	{
		RamBudget budget = RamBudget.Create(totalBytes, freeBytes, megabytes);
		MinecraftState state = State();
		state.RamMb = budget.ChosenMb;
		state.RamSpecified = true;
		state.RamPending = true;
		state.Save(NorcomPaths.NorcomHome);
	}

	public string GameFolder()
	{
		return NorcomPaths.GameDir;
	}

	public string ScreenshotsFolder()
	{
		return NorcomPaths.ScreenshotsDir;
	}

	public void SetPassword(string password)
	{
		_ = password;
	}

	public void ChooseTexture(string choice)
	{
		choice = TexturePackChoice.Normalize(choice);
		MinecraftState state = State();
		state.TexturePack = choice;
		state.TexturePackSpecified = true;
		state.TexturePackPending = true;
		state.Save(NorcomPaths.NorcomHome);
		try
		{
			TexturePackChoice.ApplyPicker(NorcomPaths.GameDir, choice);
			state.TexturePackPending = false;
			state.Save(NorcomPaths.NorcomHome);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("texture pack " + ex.GetType().Name);
			throw;
		}
	}

	public string TextureLabel()
	{
		MinecraftState state = State();
		string choice = state.TexturePackSpecified ? TexturePackChoice.Normalize(state.TexturePack) : TexturePackChoice.Detect(TexturePackChoice.ReadEnabled(NorcomPaths.GameDir));
		return "Using " + choice + ".";
	}

	public string TextureHint()
	{
		MinecraftState state = State();
		string choice = state.TexturePackSpecified ? TexturePackChoice.Normalize(state.TexturePack) : TexturePackChoice.Detect(TexturePackChoice.ReadEnabled(NorcomPaths.GameDir));
		return TexturePackChoice.Hint(NorcomPaths.GameDir, choice);
	}

	public async Task PlayAsync(IPlayContext context)
	{
		if (context.IsBusy || context.HandoffScheduled)
		{
			context.StatusLine = PlayConductor.AlreadyStarting;
			return;
		}
		context.SetBusy(true);
		bool ownsPin = false;
		CancellationTokenSource pump = null;
		try
		{
			if (!await context.AcquirePin(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
			{
				context.ProgressLabel = "Play didn't finish";
				context.StatusLine = "Another launcher task is still using the pack. Wait for it to finish, or restart NORCOM, then press Play.";
				return;
			}
			ownsPin = true;
			context.ProgressFraction = 0.02;
			context.ProgressLabel = "Preparing NORCOM";
			TransferMeter meter = new TransferMeter();
			pump = context.StartMeter(meter);
			MinecraftState state = State();
			PlayOutcome outcome = await PlayConductor.MinecraftAsync(new MinecraftPlayRequest
			{
				LockPath = NorcomPaths.GameLockPath,
				ExistingInstall = PlayInstall.HasLocalClient(NorcomPaths.GameDir),
				PollPin = async token => await context.PollPin(token).ConfigureAwait(false),
				EnsureJava = async token =>
				{
					string path = await JavaRuntime.EnsureAsync(context.Http, meter, token).ConfigureAwait(false);
					if (PlayStages.StopBeforeLaunch(NorcomPaths.DisposableProfile, Environment.GetEnvironmentVariable("NORCOM_STOP_AFTER"), "java"))
					{
						throw new PlayStoppedException("Java 25 is ready", "Stopped after Java. Minecraft was not started.");
					}
					return path;
				},
				SyncPack = async token =>
				{
					context.ProgressLabel = "Syncing the friends pack";
					await new PackSyncService(context.Http).SyncAsync(NorcomPaths.GameDir, NorcomPaths.ManagedManifestPath, NorcomPaths.PackwizCacheDir, "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml", meter, token).ConfigureAwait(false);
					ServersDat.Ensure(NorcomPaths.GameDir, "NORCOM", "shrimp-cub.tun.ply.gg");
					await context.LoadNews().ConfigureAwait(false);
					await context.LoadDiscord().ConfigureAwait(false);
					if (PlayStages.StopBeforeLaunch(NorcomPaths.DisposableProfile, Environment.GetEnvironmentVariable("NORCOM_STOP_AFTER"), "sync"))
					{
						throw new PlayStoppedException("Pack ready", "Stopped after the pack sync. Minecraft was not started.");
					}
				},
				AfterSync = () => Prepare(state),
				PreparePrism = java =>
				{
					PrismPreparation prepared = PrismPlay.Prepare(Environment.GetEnvironmentVariable("NORCOM_PRISM_EXE"), PrismInstall.Candidates(NorcomPaths.LocalAppData, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)), PrismCommand.DefaultDataDir(NorcomPaths.AppData), NorcomPaths.GameDir, java, context.MemoryMb, state.RamPending);
					if (prepared.Applied && state.RamPending)
					{
						state.RamPending = false;
						state.Save(NorcomPaths.NorcomHome);
					}
					return prepared;
				},
				RunPrism = async (prepared, token) =>
				{
					context.ProgressLabel = "Opening Prism";
					context.AccountStatus = PrismMessages.Opening;
					context.StatusLine = PrismMessages.Opening;
					context.AttachObservers();
					if (context.LauncherSounds)
					{
						context.PlaySound();
					}
					context.ArmWatch();
					return await PrismPlay.RunAsync(prepared, new PrismProcess(), token).ConfigureAwait(false);
				},
				ClientRunning = () => context.GameRunning
			}, CancellationToken.None).ConfigureAwait(false);
			context.Apply(outcome);
		}
		catch (Exception ex)
		{
			context.ProgressFraction = 0.0;
			context.ProgressLabel = "Play didn't finish";
			context.ProgressDetail = "";
			context.StatusLine = Redactor.Clean(ex.Message);
			LauncherLog.Write("Play failed: " + ex.GetType().Name);
		}
		finally
		{
			context.DetachObservers();
			context.StopMeter(pump);
			context.ReleasePin(ownsPin);
			context.SetBusy(context.HandoffScheduled);
			context.RefreshLaunched();
		}
	}

	private void Prepare(MinecraftState state)
	{
		try
		{
			bool applied = TexturePackChoice.PrepareForLaunch(NorcomPaths.GameDir, state.TexturePack, state.TexturePackSpecified, state.TexturePackPending);
			if (state.TexturePackPending && applied)
			{
				state.TexturePackPending = false;
				state.Save(NorcomPaths.NorcomHome);
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("texture pack " + ex.GetType().Name);
		}
		try
		{
			string note = GameLaunchGraphics.ApplyBeforeLaunch(Id, NorcomPaths.GameDir, NorcomPaths.NorcomHome, NorcomPaths.GameLockPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
			if (!string.IsNullOrWhiteSpace(note))
			{
				LauncherLog.Write(note);
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("graphics profile " + ex.GetType().Name);
		}
	}

	private MinecraftState State()
	{
		return _state ??= MinecraftState.Load(NorcomPaths.NorcomHome);
	}

	private sealed class MinecraftBoard : IGameBoard
	{
		public string Id => MinecraftGraphics.Id;

		public string Heading => "Minecraft";

		public bool ShowTexturePacks => true;

		public GameDefinition Definition => MinecraftGraphics.Definition;

		public string ConfigDirectory(string gameDir, string userProfile)
		{
			_ = userProfile;
			return Path.Combine(gameDir, "config");
		}

		public IReadOnlyList<DiscoveredModFile> DiscoverMods(string configDirectory, string schema)
		{
			return ModSettingsCatalog.DiscoverFeed(configDirectory, PreserveIndex(), schema);
		}

		public string Apply(SavedGraphicsProfile profile, string gameDir, string userProfile)
		{
			return MinecraftSettings.Apply(profile, gameDir, userProfile);
		}

		public void RefreshFeed(Action rebuild)
		{
			_ = rebuild;
		}

		private static string PreserveIndex()
		{
			string cached = Path.Combine(NorcomPaths.PackwizCacheDir, "index.toml");
			if (File.Exists(cached))
			{
				return File.ReadAllText(cached);
			}
			if (!File.Exists(NorcomPaths.ManagedManifestPath))
			{
				return "";
			}
			try
			{
				List<ManagedEntry> entries = JsonSerializer.Deserialize<List<ManagedEntry>>(File.ReadAllText(NorcomPaths.ManagedManifestPath));
				if (entries == null)
				{
					return "";
				}
				StringBuilder builder = new StringBuilder();
				for (int i = 0; i < entries.Count; i++)
				{
					if (!entries[i].Preserve)
					{
						continue;
					}
					builder.Append("[[files]]\nfile = \"").Append(entries[i].RelativePath).Append("\"\npreserve = true\n");
				}
				return builder.ToString();
			}
			catch (JsonException)
			{
				return "";
			}
		}
	}
}
