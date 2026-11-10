using System;
using Norcom.Launcher.Core.Updates;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Services;

namespace Norcom.Launcher.ViewModels;

public class ShellViewModel : ObservableObject
{
	private readonly HttpClient _http = QaGates.OfflineCheckRequested() ? QaGates.OfflineClient() : new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(30.0)
	};

	private readonly HttpClient _crashHttp = QaGates.OfflineCheckRequested() ? QaGates.OfflineClient() : new HttpClient(new HttpClientHandler
	{
		AllowAutoRedirect = false
	})
	{
		Timeout = TimeSpan.FromSeconds(12.0)
	};

	private readonly IGameProcessLookup _gameLookup;
    private readonly Func<HttpClient,string,TransferMeter,CancellationToken,Task> _ensureVisualAssets;
    private readonly HttpClient _metadataHttp = QaGates.OfflineCheckRequested()?QaGates.OfflineClient():new HttpClient{Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=262144};

	private readonly HttpClient _authHttp = QaGates.OfflineCheckRequested() ? QaGates.OfflineClient() : MinecraftServicesAuth.CreateHttp();

	private readonly GraphicsService _graphics = new GraphicsService();

	private readonly LauncherSettings _settings;
    private bool _loadingSettings=true;
	private bool _syncingRam;
	private MachineSpecs _specs = new MachineSpecs();

	private readonly NorcomSignIn _signIn;

	private CancellationTokenSource? _pump;

	private long _progressEpoch;

	private int? _launchedGamePid;

	private DateTime _gameWatchUntilUtc;

	private CancellationTokenSource? _authCts;

	private string? _packDiscordText;

	private readonly SemaphoreSlim _pinLock = new SemaphoreSlim(1, 1);

	private int _pinFixQueued;

	private string _pinStatus = "fetching";

	private string _pinDetail = "Checking the live server pin";

	[ObservableProperty]
	private LauncherPage page;

	[ObservableProperty]
	private string newsText = "";

	[ObservableProperty]
	private string statusLine = "";

	[ObservableProperty]
	private string progressLabel = "Ready";

	[ObservableProperty]
	private string progressDetail = "";

	[ObservableProperty]
	private double progressFraction;

	[ObservableProperty]
	private bool isBusy;

	[ObservableProperty]
	private bool gameRunning;

	[ObservableProperty]
	private bool isOnline;

	[ObservableProperty]
	private string serverHeadline = "Checking server";

	[ObservableProperty]
	private string playersText = "—";

	[ObservableProperty]
	private string motd = "";

	[ObservableProperty]
	private string latencyText = "";

	[ObservableProperty]
	private bool discordEnabled;

	[ObservableProperty]
	private string? discordUrl;

	[ObservableProperty]
	private string discordInvite = "";

	[ObservableProperty]
	private bool signedIn;

	[ObservableProperty]
	private string accountStatus = "Sign in with Microsoft in Settings. NORCOM does not read the official launcher account, and Play does not start without a verified Minecraft profile.";

	[ObservableProperty]
	private string deviceCodeText = "";

	[ObservableProperty]
	private string crashChannelStatus = "Crash channel is not configured.";

	[ObservableProperty]
	private GraphicsTier selectedTier = GraphicsTier.High;

	[ObservableProperty]
	private bool autoOn;

	[ObservableProperty]
	private double memoryMb = 6144.0;

	[ObservableProperty]
	private bool launcherSounds;

	[ObservableProperty]
	private bool closeToTray = true;

	[ObservableProperty]
	private bool reduceMotion;

	[ObservableProperty]
	private bool pauseWhilePlaying = true;

	[ObservableProperty]
	private double neonPhase;

	[ObservableProperty]
	private double glow = 0.8;

	[ObservableProperty]
	private bool animationsPaused;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<LauncherPage>? selectPageCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? playCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<GraphicsTier>? applyTierCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? applyAutoCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? fixVisualsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? repairCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? checkUpdatesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshServerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openDiscordCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openGameFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openScreenshotsFolderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? sendDiagnosticCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openPrismDownloadCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? signInBrowserCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? signInDeviceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelSignInCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? signOutCommand;

	public string VersionText => "Launcher " + NorcomVersion.Current;

	public string Wordmark => "NORCOM";

	public bool IsHome => Page == LauncherPage.Home;

	public bool IsSettings => Page == LauncherPage.Settings;

	public bool IsGraphics => Page == LauncherPage.Graphics;
	public bool IsMinecraftPage => Page == LauncherPage.Minecraft;
	public bool IsZomboidPage => Page == LauncherPage.Zomboid;

	public bool CanPlay
	{
		get
		{
			if (QaGates.OfflineCheckRequested()) return false;
			if (!IsBusy)
			{
				return !GameRunning;
			}
			return false;
		}
	}

	public string PlayAccountText => AccountStatus;

	public string PlayLabel => IsZomboid ? "Play Project Zomboid" : LaunchedGame.PlayCaption(GameRunning);
    public string PlayAvailability => QaGates.OfflineCheckRequested()?"Play is disabled for this offline verification session.":IsZomboid?ZomboidAvailability():GameRunning?"Minecraft is already running.":IsBusy?"Please wait for the current operation to finish.":"Microsoft sign-in is handled in Prism when needed.";
	public bool IsZomboid => string.Equals(SelectedGameId, ChosenGame.Zomboid, StringComparison.OrdinalIgnoreCase);
	public bool CanStopSelectedGame => IsZomboid ? GameSession.IsRunning(ChosenGame.Zomboid, NorcomPaths.GameLockPath) : GameRunning;
	public string SessionNote => IsZomboid ? "Play syncs the server's mod list from the pack, then connects in Steam. The first time, subscribe to any Workshop mods NORCOM names. You only need to own Project Zomboid." : "Play checks the client and opens your NORCOM instance in Prism.";
	public string PasswordPrompt => IsZomboid ? "Server password, saved on this PC only. Leave blank if the server has none." : "Server password is not used for Minecraft.";
	public string AccountTitle => IsZomboid ? "Owned on Steam" : "Managed in Prism";
	public string AccountBody => IsZomboid ? "NORCOM opens Project Zomboid through Steam. Sign in to Steam on this PC. NORCOM does not read your Steam password." : "Sign in with Microsoft in Prism when prompted. NORCOM opens your instance there and does not read Prism’s accounts.";
	public string AccountFine => IsZomboid ? "Workshop mods are subscribed in Steam." : "Prism Launcher is a separate GPL program.";
	public string AccountAction => IsZomboid ? "Open Steam" : "Get Prism Launcher";
	public string ClientPackDetail => IsZomboid ? "Project Zomboid files are checked when you press Play." : PinDetail;
	private string _pendingZomboidPassword = "";
	private bool _zomboidPasswordTouched;
	public void SetPendingZomboidPassword(string password)
	{
		_pendingZomboidPassword = password ?? "";
		_zomboidPasswordTouched = true;
	}
	public string GamePickerDetail => _gamePickerDetail;
	private string _selectedGameId;
	private bool _selectedGameLoaded;
	private string _gamePickerDetail = "Minecraft uses the NORCOM pack in Prism.";
	private RelayCommand<string> _selectGameCommand;
	public string SelectedGameId
	{
		get
		{
			if (!_selectedGameLoaded)
			{
				_selectedGameLoaded = true;
				_selectedGameId = ChosenGame.Load(NorcomPaths.NorcomHome);
				RefreshGamePicker();
			}
			return _selectedGameId;
		}
	}
	public IRelayCommand<string> SelectGameCommand => _selectGameCommand ??= new RelayCommand<string>(SelectGame);
    public bool CanManageClient => !QaGates.OfflineCheckRequested() && !IsBusy && !GameRunning && !BridgeRuntime.HandoffScheduled;
    private int _statusRefreshing;
    public string ServerCheckedAt {get;private set;}="Not checked yet";

	public string PinStatus => _pinStatus;

	public string PinDetail => _pinDetail;

	public string MemoryLabel => $"{(int)MemoryMb} MB";
	private RamBudget GameBudget(int requested) => RamBudget.Create(_specs.RamBytes, _specs.FreeRamBytes, requested);
	public int RamFloorMb => GameBudget(0).FloorMb;
	public int RamSafeMaxMb => GameBudget(0).SafeMaxMb;
	public int RamTotalMb => GameBudget(0).TotalMb;
	public string RamScaleCaption => GameBudget((int)MemoryMb).Caption;
	public string RamWarning => GameBudget((int)MemoryMb).Warning;
	public string SpecGpu => string.IsNullOrWhiteSpace(_specs.GpuName) ? "GPU: not detected on this PC" : "GPU: " + _specs.GpuName.Trim();
	public string SpecVram => _specs.VramBytes > 0 ? "Video memory: " + RamBudget.FormatBytes(_specs.VramBytes) : "Video memory: not detected on this PC";
	public string SpecRam => _specs.RamBytes > 0 ? "Memory: " + RamBudget.FormatBytes(_specs.RamBytes) + (_specs.FreeRamBytes > 0 ? " (" + RamBudget.FormatBytes(_specs.FreeRamBytes) + " free)" : "") : "Memory: not detected on this PC";
	public string SpecCpu => string.IsNullOrWhiteSpace(_specs.CpuName) ? "CPU: not detected on this PC" : "CPU: " + _specs.CpuName.Trim();
	public string SpecCores
	{
		get
		{
			int threads = _specs.LogicalCores;
			int cores = _specs.PhysicalCores > 0 ? _specs.PhysicalCores : threads;
			return threads > 0 ? "Cores: " + cores.ToString(System.Globalization.CultureInfo.InvariantCulture) + " cores, " + threads.ToString(System.Globalization.CultureInfo.InvariantCulture) + " threads" : "Cores: not detected on this PC";
		}
	}

	public string TierSummary
	{
		get
		{
			if (!AutoOn)
			{
				return GraphicsPresets.TierLabel(SelectedTier) + " is saved. In-game changes stay until you pick a tier again or Fix Visuals.";
			}
			return "Auto is on. It last chose " + GraphicsPresets.TierLabel(SelectedTier) + ". Fix Visuals reads this PC again.";
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public LauncherPage Page
	{
		get
		{
			return page;
		}
		set
		{
			if (!EqualityComparer<LauncherPage>.Default.Equals(page, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Page);
				page = value;
				OnPageChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Page);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewsText
	{
		get
		{
			return newsText;
		}
		[MemberNotNull("newsText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(newsText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewsText);
				newsText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewsText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusLine
	{
		get
		{
			return statusLine;
		}
		[MemberNotNull("statusLine")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(statusLine, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusLine);
				statusLine = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusLine);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ProgressLabel
	{
		get
		{
			return progressLabel;
		}
		[MemberNotNull("progressLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(progressLabel, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ProgressLabel);
				progressLabel = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ProgressLabel);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ProgressDetail
	{
		get
		{
			return progressDetail;
		}
		[MemberNotNull("progressDetail")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(progressDetail, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ProgressDetail);
				progressDetail = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ProgressDetail);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double ProgressFraction
	{
		get
		{
			return progressFraction;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(progressFraction, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ProgressFraction);
				progressFraction = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ProgressFraction);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBusy
	{
		get
		{
			return isBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isBusy, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsBusy);
				isBusy = value;
				OnIsBusyChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsBusy);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool GameRunning
	{
		get
		{
			return gameRunning;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(gameRunning, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GameRunning);
				gameRunning = value;
				OnGameRunningChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GameRunning);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsOnline
	{
		get
		{
			return isOnline;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(isOnline, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsOnline);
				isOnline = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsOnline);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ServerHeadline
	{
		get
		{
			return serverHeadline;
		}
		[MemberNotNull("serverHeadline")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(serverHeadline, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ServerHeadline);
				serverHeadline = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ServerHeadline);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PlayersText
	{
		get
		{
			return playersText;
		}
		[MemberNotNull("playersText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(playersText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PlayersText);
				playersText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PlayersText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Motd
	{
		get
		{
			return motd;
		}
		[MemberNotNull("motd")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(motd, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Motd);
				motd = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Motd);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LatencyText
	{
		get
		{
			return latencyText;
		}
		[MemberNotNull("latencyText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(latencyText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LatencyText);
				latencyText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LatencyText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool DiscordEnabled
	{
		get
		{
			return discordEnabled;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(discordEnabled, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DiscordEnabled);
				discordEnabled = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscordEnabled);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? DiscordUrl
	{
		get
		{
			return discordUrl;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(discordUrl, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DiscordUrl);
				discordUrl = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscordUrl);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DiscordInvite
	{
		get
		{
			return discordInvite;
		}
		[MemberNotNull("discordInvite")]
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<string>.Default.Equals(discordInvite, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DiscordInvite);
				discordInvite = value;
				OnDiscordInviteChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscordInvite);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool SignedIn
	{
		get
		{
			return signedIn;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(signedIn, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SignedIn);
				signedIn = value;
				OnSignedInChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SignedIn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AccountStatus
	{
		get
		{
			return accountStatus;
		}
		[MemberNotNull("accountStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(accountStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AccountStatus);
				accountStatus = value;
				OnAccountStatusChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AccountStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DeviceCodeText
	{
		get
		{
			return deviceCodeText;
		}
		[MemberNotNull("deviceCodeText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(deviceCodeText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DeviceCodeText);
				deviceCodeText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DeviceCodeText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CrashChannelStatus
	{
		get
		{
			return crashChannelStatus;
		}
		[MemberNotNull("crashChannelStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(crashChannelStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CrashChannelStatus);
				crashChannelStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CrashChannelStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public GraphicsTier SelectedTier
	{
		get
		{
			return selectedTier;
		}
		set
		{
			if (!EqualityComparer<GraphicsTier>.Default.Equals(selectedTier, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedTier);
				selectedTier = value;
				OnSelectedTierChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedTier);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AutoOn
	{
		get
		{
			return autoOn;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(autoOn, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AutoOn);
				autoOn = value;
				OnAutoOnChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AutoOn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double MemoryMb
	{
		get
		{
			return memoryMb;
		}
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<double>.Default.Equals(memoryMb, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.MemoryMb);
				memoryMb = value;
				OnMemoryMbChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.MemoryMb);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool LauncherSounds
	{
		get
		{
			return launcherSounds;
		}
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<bool>.Default.Equals(launcherSounds, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LauncherSounds);
				launcherSounds = value;
				OnLauncherSoundsChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LauncherSounds);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CloseToTray
	{
		get
		{
			return closeToTray;
		}
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<bool>.Default.Equals(closeToTray, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CloseToTray);
				closeToTray = value;
				OnCloseToTrayChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CloseToTray);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ReduceMotion
	{
		get
		{
			return reduceMotion;
		}
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<bool>.Default.Equals(reduceMotion, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ReduceMotion);
				reduceMotion = value;
				OnReduceMotionChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ReduceMotion);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PauseWhilePlaying
	{
		get
		{
			return pauseWhilePlaying;
		}
		set
		{
			if (IsBusy || BridgeRuntime.HandoffScheduled) return;
			if (!EqualityComparer<bool>.Default.Equals(pauseWhilePlaying, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PauseWhilePlaying);
				pauseWhilePlaying = value;
				OnPauseWhilePlayingChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PauseWhilePlaying);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double NeonPhase
	{
		get
		{
			return neonPhase;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(neonPhase, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NeonPhase);
				neonPhase = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NeonPhase);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double Glow
	{
		get
		{
			return glow;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(glow, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Glow);
				glow = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Glow);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AnimationsPaused
	{
		get
		{
			return animationsPaused;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(animationsPaused, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AnimationsPaused);
				animationsPaused = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AnimationsPaused);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<LauncherPage> SelectPageCommand => (IRelayCommand<LauncherPage>)(object)(selectPageCommand ?? (selectPageCommand = new RelayCommand<LauncherPage>((Action<LauncherPage>)SelectPage)));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PlayCommand
	{
		get
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Expected O, but got Unknown
			//IL_002f: Expected O, but got Unknown
			AsyncRelayCommand obj = playCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)PlayAsync, (Func<bool>)(() => CanPlay));
				AsyncRelayCommand val2 = val;
				playCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<GraphicsTier> ApplyTierCommand => (IRelayCommand<GraphicsTier>)(object)(applyTierCommand ?? (applyTierCommand = new RelayCommand<GraphicsTier>((Action<GraphicsTier>)ApplyTier)));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ApplyAutoCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			RelayCommand obj = applyAutoCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)ApplyAuto);
				RelayCommand val2 = val;
				applyAutoCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand FixVisualsCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			AsyncRelayCommand obj = fixVisualsCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)FixVisualsAsync);
				AsyncRelayCommand val2 = val;
				fixVisualsCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RepairCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			AsyncRelayCommand obj = repairCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)RepairAsync);
				AsyncRelayCommand val2 = val;
				repairCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CheckUpdatesCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			AsyncRelayCommand obj = checkUpdatesCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)CheckUpdatesAsync);
				AsyncRelayCommand val2 = val;
				checkUpdatesCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshServerCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			AsyncRelayCommand obj = refreshServerCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)RefreshServer);
				AsyncRelayCommand val2 = val;
				refreshServerCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenDiscordCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			RelayCommand obj = openDiscordCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)OpenDiscord);
				RelayCommand val2 = val;
				openDiscordCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenGameFolderCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			RelayCommand obj = openGameFolderCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)OpenGameFolder);
				RelayCommand val2 = val;
				openGameFolderCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenScreenshotsFolderCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			RelayCommand obj = openScreenshotsFolderCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)OpenScreenshotsFolder);
				RelayCommand val2 = val;
				openScreenshotsFolderCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SendDiagnosticCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			AsyncRelayCommand obj = sendDiagnosticCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)SendDiagnostic);
				AsyncRelayCommand val2 = val;
				sendDiagnosticCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenPrismDownloadCommand
	{
		get
		{
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_001e: Expected O, but got Unknown
			//IL_0023: Expected O, but got Unknown
			RelayCommand obj = openPrismDownloadCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)OpenPrismDownload);
				RelayCommand val2 = val;
				openPrismDownloadCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SignInBrowserCommand
	{
		get
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Expected O, but got Unknown
			//IL_002f: Expected O, but got Unknown
			AsyncRelayCommand obj = signInBrowserCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)SignInBrowserAsync, (Func<bool>)CanSignIn);
				AsyncRelayCommand val2 = val;
				signInBrowserCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SignInDeviceCommand
	{
		get
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Expected O, but got Unknown
			//IL_002f: Expected O, but got Unknown
			AsyncRelayCommand obj = signInDeviceCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)SignInDeviceAsync, (Func<bool>)CanSignIn);
				AsyncRelayCommand val2 = val;
				signInDeviceCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelSignInCommand
	{
		get
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Expected O, but got Unknown
			//IL_002f: Expected O, but got Unknown
			RelayCommand obj = cancelSignInCommand;
			if (obj == null)
			{
				RelayCommand val = new RelayCommand((Action)CancelSignIn, (Func<bool>)CanCancelSignIn);
				RelayCommand val2 = val;
				cancelSignInCommand = val;
				obj = val2;
			}
			return (IRelayCommand)(object)obj;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SignOutCommand
	{
		get
		{
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Expected O, but got Unknown
			//IL_002f: Expected O, but got Unknown
			AsyncRelayCommand obj = signOutCommand;
			if (obj == null)
			{
				AsyncRelayCommand val = new AsyncRelayCommand((Func<Task>)SignOutAsync, (Func<bool>)CanSignOut);
				AsyncRelayCommand val2 = val;
				signOutCommand = val;
				obj = val2;
			}
			return (IAsyncRelayCommand)(object)obj;
		}
	}

	public ShellViewModel():this(null){}

    internal ShellViewModel(IGameProcessLookup gameLookup, Func<HttpClient,string,TransferMeter,CancellationToken,Task> ensureVisualAssets=null)
	{
        _ensureVisualAssets=ensureVisualAssets??KnownVisualAssets.EnsureAsync;
        _gameLookup=gameLookup??new LivePrismGameSignals(PrismCommand.DefaultDataDir(NorcomPaths.AppData),NorcomPaths.GameDir);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(NorcomVersion.UserAgent);
		_settings = LauncherSettings.Load(NorcomPaths.SettingsPath);
		Directory.CreateDirectory(NorcomPaths.GameDir);
		Directory.CreateDirectory(NorcomPaths.LauncherDir);
		try
		{
			_specs = MachineSpecsProbe.Read();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("specs " + ex.GetType().Name);
			_specs = new MachineSpecs();
		}
		ShowGameMemory();
		SelectedTier = _settings.GraphicsTier;
		AutoOn = _settings.GraphicsAuto;
		LauncherSounds = _settings.LauncherSounds;
		CloseToTray = _settings.CloseToTray;
		ReduceMotion = _settings.ReduceMotion;
		PauseWhilePlaying = _settings.AnimationsPausedWhilePlaying;
		DiscordInvite = _settings.DiscordInviteUrl;
		FileAuthStore store = AuthStores.Create(NorcomPaths.AuthDir);
		_signIn = new NorcomSignIn(new MsalMicrosoftAccount(store), new MinecraftServicesAuth(_authHttp), store, NorcomPaths.AuthDir);
		AccountStatus = "Microsoft sign-in is handled by Prism in its normal folder. Play starts the NORCOM instance and joins the NORCOM server. Sign in once in that Prism window if it asks. Prism may show a window. Prism Launcher is a separate GPL program, not part of NORCOM. NORCOM does not read Prism's accounts.";
		RefreshCrashChannelStatus();
		NewsText = NewsComposer.Compose(NorcomVersion.Current, null, null, null);
		StatusLine = "Launcher " + NorcomVersion.Current;
		StartupHeal.ReleaseForeignClientLock(NorcomPaths.GameLockPath);
		GameRunning = StartupHeal.GameLockAlive(NorcomPaths.GameLockPath);
		AnimationsPaused = GameRunning && PauseWhilePlaying;
		PlayAccount.Install(this);
        _loadingSettings=false;
	}

	public void AdvanceClock()
	{
		if (!AnimationsPaused && !ReduceMotion)
		{
			NeonPhase = (NeonPhase + 0.35) % 1000.0;
			Glow = 0.4 + 0.6 * Math.Abs(Math.Sin(NeonPhase * 0.08));
		}
	}

	[RelayCommand]
	private void SelectPage(LauncherPage next)
	{
		Page = next;
		if (next == LauncherPage.Minecraft)
		{
			SelectGame(ChosenGame.Minecraft);
		}
		else if (next == LauncherPage.Zomboid)
		{
			SelectGame(ChosenGame.Zomboid);
		}
	}

	public async Task InitializeAsync()
	{
		if (QaGates.OfflineCheckRequested())
		{
			ServerHeadline = "Server status unknown";
			Motd = "Offline verification profile. No server connection was attempted.";
			SetPinStatus("unavailable", "Live pin checks are paused in this verification profile.");
			return;
		}
		// Queued reports remain local until the user explicitly sends diagnostics.
		await Task.WhenAll(RefreshServerAsync(), LoadNewsAsync(), LoadDiscordAsync()).ConfigureAwait(continueOnCapturedContext: true);
	}

	public Task PollLivePinAsync(bool force) => PollLivePinCoreAsync(force, ownsPinLock: false);

	private async Task PollLivePinCoreAsync(bool force, bool ownsPinLock)
	{
		if (QaGates.OfflineCheckRequested() || GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath)) return;
		if ((IsBusy && !ownsPinLock) || BridgeRuntime.HandoffScheduled) return;
		if (force)
		{
			Interlocked.Exchange(ref _pinFixQueued, 1);
			SetPinStatus("fetching", "Fixing the live server pin");
		}
		if (!ownsPinLock && !await _pinLock.WaitAsync(0).ConfigureAwait(continueOnCapturedContext: true))
		{
			if (!force)
			{
				return;
			}
			await _pinLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: true);
		}
		try
		{
			if ((IsBusy && !ownsPinLock) || BridgeRuntime.HandoffScheduled) return;
			bool again = true;
			while (again)
			{
				bool forced = force || Interlocked.Exchange(ref _pinFixQueued, 0) == 1;
				force = false;
				SetPinStatus("fetching", forced ? "Fixing the live server pin" : "Checking the live server pin");
				try
				{
					PinGate.PinSyncResult result = await PinGate.SyncPublishedAsync(_http, NorcomPaths.GameDir, CancellationToken.None, forced, NorcomPaths.LauncherDir).ConfigureAwait(continueOnCapturedContext: true);
					SetPinStatus(result.State, result.Detail);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("pin poll " + ex.GetType().Name);
					SetPinStatus("mismatch", forced ? "Fix could not heal the live server pin" : "published pin could not be fetched");
				}
				again = Volatile.Read(ref _pinFixQueued) == 1;
			}
		}
		finally
		{
			if (!ownsPinLock) _pinLock.Release();
		}
	}

	public Task FixPinAsync()
	{
		return PollLivePinAsync(force: true);
	}

	private void SetPinStatus(string state, string detail)
	{
		_pinStatus = string.IsNullOrWhiteSpace(state) ? "mismatch" : state.Trim();
		_pinDetail = detail ?? "";
		OnPropertyChanged(nameof(PinStatus));
		OnPropertyChanged(nameof(PinDetail));
		OnPropertyChanged(nameof(ClientPackDetail));
	}

	public async Task<bool> ApplyUpdateIfNeededAsync(Action<string, double> report, CancellationToken ct, bool allowRetry = false)
	{
		if (IsBusy || BridgeRuntime.HandoffScheduled)
		{
			report("Finish the current launcher task before updating.", 1);
			return false;
		}
		IsBusy = true;
		bool ownsPinLock = false;
		try
		{
			await _pinLock.WaitAsync(ct).ConfigureAwait(true);
			ownsPinLock = true;
			return await BridgeRuntime.CheckAndScheduleAsync((text, progress) =>
			{
				StatusLine = text;
				report(text, progress);
			}, ct, allowRetry).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			StatusLine = "Launcher update unavailable. " + Redactor.Clean(ex.Message);
			report(StatusLine, 1.0);
			return false;
		}
		finally { if (ownsPinLock) _pinLock.Release(); IsBusy = BridgeRuntime.HandoffScheduled; }
	}

	private void NoteUpdate(string token)
	{
		UpdateReport.Write(NorcomPaths.LauncherDir, token);
		StatusLine = "Launcher " + NorcomVersion.Current + ". " + token;
	}

	[RelayCommand(CanExecute = "CanPlay")]
	private async Task PlayAsync()
	{
		if (QaGates.OfflineCheckRequested()) return;
		if (string.Equals(SelectedGameId, ChosenGame.Zomboid, StringComparison.OrdinalIgnoreCase))
		{
			await PlayZomboidAsync().ConfigureAwait(continueOnCapturedContext: true);
			return;
		}
		if (GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))
		{
			StatusLine = "Close Minecraft and confirm its status before changing the game instance.";
			return;
		}
		if (IsBusy || BridgeRuntime.HandoffScheduled) return;
		IsBusy = true;
		bool ownsPinLock = false;
		CancellationTokenSource pump = null;
		try
		{
			await _pinLock.WaitAsync().ConfigureAwait(true);
			ownsPinLock = true;
		ProgressFraction = 0.02;
		ProgressLabel = "Preparing NORCOM";
		TransferMeter meter = new TransferMeter();
		pump = StartPump(meter);
			await PollLivePinCoreAsync(force: true, ownsPinLock: true).ConfigureAwait(true);
			string java = await JavaRuntime.EnsureAsync(_http, meter, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			if (PlayStages.StopBeforeLaunch(NorcomPaths.DisposableProfile, Environment.GetEnvironmentVariable("NORCOM_STOP_AFTER"), "java"))
			{
				ProgressLabel = "Java 25 is ready";
				StatusLine = "Stopped after Java. Minecraft was not started.";
				return;
			}
			ProgressLabel = "Syncing the friends pack";
			await new PackSyncService(_http).SyncAsync(NorcomPaths.GameDir, NorcomPaths.ManagedManifestPath, NorcomPaths.PackwizCacheDir, "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml", meter, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			ServersDat.Ensure(NorcomPaths.GameDir, "NORCOM", "shrimp-cub.tun.ply.gg");
			await LoadNewsAsync().ConfigureAwait(continueOnCapturedContext: true);
			await LoadDiscordAsync().ConfigureAwait(continueOnCapturedContext: true);
			if (PlayStages.StopBeforeLaunch(NorcomPaths.DisposableProfile, Environment.GetEnvironmentVariable("NORCOM_STOP_AFTER"), "sync"))
			{
				ProgressLabel = "Pack ready";
				StatusLine = "Stopped after the pack sync. Minecraft was not started.";
				return;
			}
			try
			{
				string graphicsNote = GameLaunchGraphics.ApplyBeforeLaunch("minecraft", NorcomPaths.GameDir, NorcomPaths.NorcomHome, NorcomPaths.GameLockPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
				if (!string.IsNullOrWhiteSpace(graphicsNote))
				{
					LauncherLog.Write(graphicsNote);
				}
			}
			catch (Exception ex)
			{
				LauncherLog.Write("graphics profile " + ex.GetType().Name);
			}
			PrismPreparation prismPreparation = PrismPlay.Prepare(Environment.GetEnvironmentVariable("NORCOM_PRISM_EXE"), PrismInstall.Candidates(NorcomPaths.LocalAppData, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)), PrismCommand.DefaultDataDir(NorcomPaths.AppData), NorcomPaths.GameDir, java, (int)MemoryMb);
			if (!prismPreparation.CanStart || prismPreparation.Executable == null)
			{
				ProgressLabel = "Prism is required";
				AccountStatus = prismPreparation.Message;
				StatusLine = prismPreparation.Message;
				return;
			}
			ProgressLabel = "Opening Prism";
			AccountStatus = "Opening Prism for the NORCOM instance. If Prism asks, sign in with Microsoft in that window. NORCOM does not keep that login and did not start an offline game.";
			StatusLine = "Opening Prism for the NORCOM instance. If Prism asks, sign in with Microsoft in that window. NORCOM does not keep that login and did not start an offline game.";
			PrismPlay.GameObserved = ClearPackReady;
			PrismPlay.GameProcessObserved = delegate(int? pid)
			{
				if (pid is int id && id > 0)
				{
					OnUi(delegate
					{
						RememberLaunchedGame(id);
					});
				}
			};
			if (LauncherSounds)
			{
				UiSound.Play();
			}
			_gameWatchUntilUtc = DateTime.UtcNow.AddSeconds(20.0);
			PrismPreparation prismPreparation2 = await PrismPlay.RunAsync(prismPreparation, new PrismProcess(), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			_gameWatchUntilUtc = DateTime.UtcNow.AddSeconds(20.0);
			RefreshLaunchedGame();
			ProgressFraction = 1.0;
			if (GameRunning)
			{
				ProgressLabel = "Playing";
				ProgressDetail = "";
			}
			else
			{
				ProgressLabel = (prismPreparation2.CanStart ? "Prism closed" : "Prism did not start Minecraft");
				ProgressDetail = "";
				AccountStatus = prismPreparation2.Message;
				StatusLine = prismPreparation2.Message;
			}
			LauncherLog.Write(prismPreparation2.CanStart ? "Prism closed" : "Prism rejected");
		}
		catch (Exception ex)
		{
			ProgressFraction = 0.0;
			ProgressLabel = "Play didn't finish";
			ProgressDetail = "";
			StatusLine = Redactor.Clean(ex.Message);
			LauncherLog.Write("Play failed: " + ex.GetType().Name);
		}
		finally
		{
			PrismPlay.GameObserved = null;
			PrismPlay.GameProcessObserved = null;
			pump?.Cancel();
			if (ownsPinLock) _pinLock.Release();
			IsBusy = BridgeRuntime.HandoffScheduled;
			RefreshLaunchedGame();
		}
	}

	private async Task PlayZomboidAsync()
	{
		if (IsBusy || BridgeRuntime.HandoffScheduled)
		{
			return;
		}
		IsBusy = true;
		try
		{
			ProgressFraction = 0.2;
			ProgressLabel = "Checking Steam";
			string feedJson = "";
			try
			{
				feedJson = await _http.GetStringAsync(ZomboidFeed.Url).ConfigureAwait(continueOnCapturedContext: true);
			}
			catch (Exception ex)
			{
				LauncherLog.Write("zomboid feed " + ex.GetType().Name);
			}
			if (!string.IsNullOrWhiteSpace(feedJson))
			{
				try
				{
					File.WriteAllText(ZomboidFeed.CachePath(NorcomPaths.NorcomHome), feedJson);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("zomboid feed cache " + ex.GetType().Name);
				}
			}
			ProgressLabel = "Syncing Project Zomboid";
			string localPassword = RememberZomboidPassword();
			ZomboidLaunchPlan plan = await ZomboidLaunch.PrepareAsync(SteamSources.Detect(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), NorcomPaths.NorcomHome, NorcomPaths.GameLockPath, feedJson, _http, CancellationToken.None, localPassword, (int)MemoryMb).ConfigureAwait(continueOnCapturedContext: true);
			RefreshGamePicker();
			if (!string.IsNullOrWhiteSpace(plan.GraphicsNote))
			{
				LauncherLog.Write(plan.GraphicsNote);
			}
			if (!plan.CanStart)
			{
				ProgressFraction = 0.0;
				ProgressLabel = plan.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ? SteamLibrary.NotFound : "Project Zomboid was not started";
				StatusLine = plan.Message;
				return;
			}
			ProgressLabel = "Opening Steam";
			StatusLine = plan.Message;
			SteamStart.Open(plan.Url);
			ProgressFraction = 1.0;
			LauncherLog.Write(plan.IncludesPassword ? "zomboid steam connect" : "zomboid steam launch");
		}
		catch (Exception ex)
		{
			ProgressFraction = 0.0;
			ProgressLabel = "Play didn't finish";
			StatusLine = Redactor.Clean(ex.Message);
			LauncherLog.Write("zomboid play " + ex.GetType().Name);
		}
		finally
		{
			IsBusy = BridgeRuntime.HandoffScheduled;
			OnPropertyChanged(nameof(PlayAvailability));
			OnPropertyChanged(nameof(GamePickerDetail));
		}
	}

	private void SelectGame(string id)
	{
		_selectedGameLoaded = true;
		_selectedGameId = ChosenGame.Normalize(id);
		try
		{
			ChosenGame.Save(NorcomPaths.NorcomHome, _selectedGameId);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("selected game " + ex.GetType().Name);
		}
		RefreshGamePicker();
		OnPropertyChanged(nameof(SelectedGameId));
		OnPropertyChanged(nameof(IsZomboid));
		OnPropertyChanged(nameof(CanStopSelectedGame));
		OnPropertyChanged(nameof(SessionNote));
		OnPropertyChanged(nameof(PasswordPrompt));
		OnPropertyChanged(nameof(AccountTitle));
		OnPropertyChanged(nameof(AccountBody));
		OnPropertyChanged(nameof(AccountFine));
		OnPropertyChanged(nameof(AccountAction));
		OnPropertyChanged(nameof(ClientPackDetail));
		OnPropertyChanged(nameof(PlayLabel));
		OnPropertyChanged(nameof(PlayAvailability));
		OnPropertyChanged(nameof(GamePickerDetail));
		ShowGameMemory();
	}

	private void ShowGameMemory()
	{
		int requested = IsZomboid ? (_settings.ZomboidRamSpecified ? _settings.ZomboidRamMb : 0) : (_settings.RamSpecified ? _settings.RamMb : 0);
		_syncingRam = true;
		MemoryMb = GameBudget(requested).ChosenMb;
		_syncingRam = false;
		NotifyRam();
	}

	private void NotifyRam()
	{
		OnPropertyChanged(nameof(MemoryLabel));
		OnPropertyChanged(nameof(RamFloorMb));
		OnPropertyChanged(nameof(RamSafeMaxMb));
		OnPropertyChanged(nameof(RamTotalMb));
		OnPropertyChanged(nameof(RamScaleCaption));
		OnPropertyChanged(nameof(RamWarning));
	}

	private void RefreshGamePicker()
	{
		if (!string.Equals(_selectedGameId, ChosenGame.Zomboid, StringComparison.OrdinalIgnoreCase))
		{
			_gamePickerDetail = "Minecraft uses the NORCOM pack in Prism.";
			return;
		}
		SteamGameLocation install = SteamLibrary.FindZomboid(SteamSources.Detect());
		_gamePickerDetail = install.Found ? "Play checks the pack and opens Project Zomboid in Steam." : SteamLibrary.NotFound;
	}

	private string ZomboidAvailability()
	{
		if (IsBusy)
		{
			return "Please wait for the current operation to finish.";
		}
		return _gamePickerDetail;
	}

	private string RememberZomboidPassword()
	{
		if (!_zomboidPasswordTouched && ZomboidPasswordStore.Exists(NorcomPaths.NorcomHome))
		{
			return ZomboidPasswordStore.Load(NorcomPaths.NorcomHome);
		}
		ZomboidPasswordStore.Save(NorcomPaths.NorcomHome, _pendingZomboidPassword);
		return _pendingZomboidPassword;
	}

	[RelayCommand]
	private void OpenAccountAction()
	{
		if (IsZomboid)
		{
			SteamStart.Open("steam://open/main");
			return;
		}
		OpenPrismDownload();
	}

	public void RefreshLaunchedGame()
	{
		if (StartupHeal.ReleaseForeignClientLock(NorcomPaths.GameLockPath))
		{
			_launchedGamePid = null;
			if (GameRunning)
			{
				GameRunning = false;
			}
		}
		int? pid = _launchedGamePid ?? StartupHeal.ReadGamePid(NorcomPaths.GameLockPath);
		if (pid is int id && id > 0 && StartupHeal.ReadGamePid(NorcomPaths.GameLockPath)==id && StartupHeal.GameLockAlive(NorcomPaths.GameLockPath))
		{
			_launchedGamePid = id;
			if (!GameRunning)
			{
				GameRunning = true;
			}
			return;
		}
		if (GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))
		{
			StatusLine = "The game status could not be confirmed. Launcher updates remain blocked.";
			return;
		}
		if (pid != null)
		{
			_launchedGamePid = null;
			_gameWatchUntilUtc = DateTime.MinValue;
			// The conservative guard alone may clear a confirmed stale lock.
			if (GameRunning)
			{
				GameRunning = false;
				if (string.Equals(ProgressLabel, "Playing", StringComparison.Ordinal))
				{
					ProgressLabel = "Ready";
				}
				StatusLine = "Minecraft closed.";
			}
			return;
		}
		if (IsBusy || DateTime.UtcNow < _gameWatchUntilUtc)
		{
			TryAttachLaunchedGame();
		}
	}

	public void StopLaunchedGame()
	{
		int? pid = _launchedGamePid ?? StartupHeal.ReadGamePid(NorcomPaths.GameLockPath);
		if (!(pid is int id) || id <= 0)
		{
			_launchedGamePid = null;
			GameRunning = false;
			StatusLine = "No verified game process is available to stop.";
			return;
		}
        var expectedTicks=StartupHeal.ReadGameStartTicks(NorcomPaths.GameLockPath);
        if(expectedTicks is not >0){StatusLine="The recorded game process cannot be verified. Close Minecraft from its own window.";return;}
        StatusLine = "Stopping Minecraft.";
		Task.Run(delegate
		{
			bool stopped = LaunchedGame.TryStop(id,expectedTicks);
			OnUi(delegate
			{
				FinishStop(id, stopped);
			});
		});
	}

	private void FinishStop(int pid, bool stopped)
	{
        if(!stopped){StatusLine="Stop could not be confirmed. Close Minecraft from its own window.";return;}
		if (!StartupHeal.IsAlive(pid))
		{
			if (_launchedGamePid == pid)
			{
				_launchedGamePid = null;
			}
			int? locked = StartupHeal.ReadGamePid(NorcomPaths.GameLockPath);
			if (locked == null || locked == pid)
			{
				LaunchedGame.Forget(NorcomPaths.GameLockPath);
			}
		}
		bool still = (_launchedGamePid is int live && StartupHeal.IsAlive(live)) || StartupHeal.GameLockAlive(NorcomPaths.GameLockPath);
		if (!still)
		{
			_gameWatchUntilUtc = DateTime.MinValue;
		}
		GameRunning = still;
		if (!still)
		{
			if (string.Equals(ProgressLabel, "Playing", StringComparison.Ordinal) || string.Equals(ProgressLabel, "Stopping Minecraft.", StringComparison.Ordinal))
			{
				ProgressLabel = "Ready";
			}
			StatusLine = "No verified game process is available to stop.";
		}
		else
		{
			StatusLine = stopped ? "Minecraft is closing." : "Minecraft is still running.";
		}
		LauncherLog.Write("game stop " + (stopped ? "ok" : "miss") + " pid=" + pid);
	}

	private void TryAttachLaunchedGame()
	{
		try
		{
			int? pid = _gameLookup.TryGameProcessId(null);
			if (pid is int id && id > 0 && StartupHeal.IsAlive(id))
			{
				RememberLaunchedGame(id);
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("game attach " + ex.GetType().Name);
		}
	}

	private void RememberLaunchedGame(int pid)
	{
		_launchedGamePid = pid;
		LaunchedGame.Remember(NorcomPaths.GameLockPath, pid);
		if (!GameRunning)
		{
			GameRunning = true;
		}
		if (string.Equals(ProgressLabel, "Opening Prism", StringComparison.Ordinal) || string.Equals(ProgressLabel, "Prism closed", StringComparison.Ordinal))
		{
			ProgressLabel = "Playing";
		}
	}

	private void OnUi(Action action)
	{
		Application current = Application.Current;
		if (current == null || ((DispatcherObject)current).Dispatcher.CheckAccess())
		{
			action();
			return;
		}
		((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)action, Array.Empty<object>());
	}

	private void ClearPackReady()
	{
		void Clear()
		{
			if (string.Equals(ProgressLabel, "Pack ready", StringComparison.Ordinal))
			{
				ProgressLabel = "";
				ProgressDetail = "";
			}
		}
		Application current = Application.Current;
		if (current == null || ((DispatcherObject)current).Dispatcher.CheckAccess())
		{
			Clear();
			return;
		}
		((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)Clear, Array.Empty<object>());
	}

	[RelayCommand]
	private void ApplyTier(GraphicsTier tier)
	{
		ApplyResolved(tier, auto: false, "from the " + GraphicsPresets.TierLabel(tier) + " button");
	}

	[RelayCommand]
	private void ApplyAuto()
	{
		PcHardware pcHardware = new WmiPcProbe().TryRead();
		GraphicsTier tier = (((object)pcHardware == null) ? GraphicsTier.High : GraphicsAuto.Choose(pcHardware));
		string why = (((object)pcHardware == null) ? "This PC could not be read, so High was applied." : ("This PC was read as " + pcHardware.GpuName + "."));
		ApplyResolved(tier, auto: true, why);
	}

    private void ApplyResolved(GraphicsTier tier,bool auto,string why)
    {
        if(!CanManageClient || GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))return;
        if(!_pinLock.Wait(0)){StatusLine="Wait for the pack check to finish before changing graphics.";return;}
        try
        {
            if(!CanManageClient || GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))return;
            _graphics.Apply(NorcomPaths.GameDir,tier);var removed=_graphics.ClearShaderCache(NorcomPaths.GameDir);
            _settings.GraphicsTier=tier;_settings.GraphicsAuto=auto;_settings.Save(NorcomPaths.SettingsPath);
            SelectedTier=tier;AutoOn=auto;StatusLine=GraphicsPresets.TierLabel(tier)+" applied. Shader cache cleared ("+removed.Count+").";
        }
        catch(Exception ex){StatusLine="Graphics could not be changed. "+Redactor.Clean(ex.Message);}
        finally{_pinLock.Release();}
    }

	[RelayCommand]
	private async Task FixVisualsAsync()
	{
		if (QaGates.OfflineCheckRequested()) return;
		if (GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))
		{
			StatusLine = "Close Minecraft and confirm its status before changing the game instance.";
			return;
		}
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		bool ownsPinLock = false;
		CancellationTokenSource pump = null;
		try
		{
			await _pinLock.WaitAsync().ConfigureAwait(true);
			ownsPinLock = true;
		TransferMeter meter = new TransferMeter();
		pump = StartPump(meter);
			GraphicsTier tier = _settings.GraphicsTier;
			if (_settings.GraphicsAuto)
			{
				PcHardware pcHardware = new WmiPcProbe().TryRead();
				tier = (((object)pcHardware == null) ? GraphicsTier.High : GraphicsAuto.Choose(pcHardware));
				_settings.GraphicsTier = tier;
				_settings.Save(NorcomPaths.SettingsPath);
			}
			string backup = _graphics.BackupAndReset(NorcomPaths.GameDir, tier);
			await _ensureVisualAssets(_http, NorcomPaths.GameDir, meter, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			IReadOnlyList<string> readOnlyList = _graphics.ClearShaderCache(NorcomPaths.GameDir);
			SelectedTier = tier;
			ProgressFraction = 1.0;
			ProgressLabel = "Visuals repaired";
			ProgressDetail = "";
			StatusLine = "Reset to " + GraphicsPresets.TierLabel(tier) + ". Cache entries removed: " + readOnlyList.Count + ". Backup: " + backup;
		}
		catch (Exception ex)
		{
			StatusLine = "Fix Visuals stopped. " + Redactor.Clean(ex.Message);
			ProgressLabel = "Fix Visuals didn't finish";
			ProgressDetail = "";
		}
		finally
		{
			pump?.Cancel();
			if (ownsPinLock) _pinLock.Release();
			IsBusy = BridgeRuntime.HandoffScheduled;
		}
	}

	[RelayCommand]
	private async Task RepairAsync()
	{
		if (QaGates.OfflineCheckRequested()) return;
		if (GameUpdateGuard.IsBlocked(NorcomPaths.GameLockPath))
		{
			StatusLine = "Close Minecraft and confirm its status before changing the game instance.";
			return;
		}
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		bool ownsPinLock = false;
		CancellationTokenSource pump = null;
		try
		{
			await _pinLock.WaitAsync().ConfigureAwait(true);
			ownsPinLock = true;
		TransferMeter meter = new TransferMeter();
		pump = StartPump(meter);
			StartupHeal.ClearStale(NorcomPaths.LauncherDir, NorcomPaths.GameDir);
			GameRunning = StartupHeal.GameLockAlive(NorcomPaths.GameLockPath);
			string line = await RepairRun.ExecuteAsync(meter, (CancellationToken token) => new PackSyncService(_http).SyncAsync(NorcomPaths.GameDir, NorcomPaths.ManagedManifestPath, NorcomPaths.PackwizCacheDir, "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml", meter, token), (CancellationToken token) => JavaRuntime.EnsureAsync(_http, meter, token), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			ProgressFraction = 1.0;
			ProgressLabel = "Repair finished";
			ProgressDetail = line;
			StatusLine = line;
		}
		catch (Exception ex)
		{
			StatusLine = "Repair stopped. " + Redactor.Clean(ex.Message);
		}
		finally
		{
			pump?.Cancel();
			if (ownsPinLock) _pinLock.Release();
			IsBusy = BridgeRuntime.HandoffScheduled;
		}
	}

	[RelayCommand]
	private async Task CheckUpdatesAsync()
	{
		if (await ApplyUpdateIfNeededAsync(delegate
		{
		}, CancellationToken.None, allowRetry: true).ConfigureAwait(continueOnCapturedContext: true))
		{
			Application.Current.Shutdown();
		}
	}

	[RelayCommand]
	private async Task RefreshServer()
	{
		await RefreshServerAsync().ConfigureAwait(continueOnCapturedContext: true);
	}

	[RelayCommand]
	private void OpenDiscord()
	{
		if (DiscordLink.IsInvite(DiscordUrl))
		{
			Process.Start(new ProcessStartInfo(DiscordUrl)
			{
				UseShellExecute = true
			});
		}
	}

	[RelayCommand]
	private void OpenGameFolder()
	{
		if (IsZomboid)
		{
			SteamGameLocation install = SteamLibrary.FindZomboid(SteamSources.Detect());
			Open(install.Found ? install.GamePath : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid"));
			return;
		}
		Open(NorcomPaths.GameDir);
	}

	[RelayCommand]
	private void OpenScreenshotsFolder()
	{
		string folder = IsZomboid ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid", "Screenshots") : NorcomPaths.ScreenshotsDir;
		Directory.CreateDirectory(folder);
		Open(folder);
	}

	[RelayCommand]
	private async Task SendDiagnostic()
	{
		string reportPath = CrashReportService.Queue("manual", "Manual diagnostic from Settings", null);
		var delivery=await DeliverCrashAsync(reportPath).ConfigureAwait(true);
        var text=delivery.Kind==CrashReportService.DeliveryKind.SendFailed?"The report stayed on this PC. Choose Send diagnostics in Settings to retry.":delivery.UserText;
        MessageBox.Show(text,"NORCOM",MessageBoxButton.OK,MessageBoxImage.Information);
	}

    private async Task RefreshServerAsync()
    {
        if(QaGates.OfflineCheckRequested() || Interlocked.Exchange(ref _statusRefreshing,1)!=0)return;
        try
        {
            var result=await Task.Run(HomeStatusClient.Read);
            IsOnline=result.ShowOnline;ServerHeadline=result.Headline;PlayersText=result.PlayersText;
            Motd=result.Detail;LatencyText=result.Source;
            ServerCheckedAt="Last check "+DateTime.Now.ToString("HH:mm:ss");OnPropertyChanged(nameof(ServerCheckedAt));
        }
        catch(Exception ex){IsOnline=false;ServerHeadline="Status unknown";Motd="The server could not be reached. Try Refresh status.";LauncherLog.Write("status "+ex.GetType().Name);}
        finally{Interlocked.Exchange(ref _statusRefreshing,0);}
    }

	private async Task LoadNewsAsync()
	{
		string news = await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/news.md").ConfigureAwait(continueOnCapturedContext: true);
		string notes = await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/pack-notes.md").ConfigureAwait(continueOnCapturedContext: true);
		string text = await TryGet("https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml").ConfigureAwait(continueOnCapturedContext: true);
		string version = ((text == null) ? null : TomlLite.Parse(text).Keys.GetValueOrDefault("version"));
		string manifestJson = await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/launcher/manifest.json").ConfigureAwait(continueOnCapturedContext: true);
		if (!string.IsNullOrWhiteSpace(manifestJson))
		{
			try
			{
				LauncherManifest? manifest = JsonSerializer.Deserialize<LauncherManifest>(manifestJson);
				if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Notes))
				{
					UpdateNotes.Remember(NorcomPaths.LauncherDir, manifest, NorcomVersion.Current);
				}
			}
			catch (JsonException ex)
			{
				LauncherLog.Write("update notes " + ex.GetType().Name);
			}
		}
		if (news == null && File.Exists(Path.Combine(NorcomPaths.GameDir, "news.md")))
		{
			news = await File.ReadAllTextAsync(Path.Combine(NorcomPaths.GameDir, "news.md")).ConfigureAwait(continueOnCapturedContext: true);
		}
		if (notes == null && File.Exists(Path.Combine(NorcomPaths.GameDir, "pack-notes.md")))
		{
			notes = await File.ReadAllTextAsync(Path.Combine(NorcomPaths.GameDir, "pack-notes.md")).ConfigureAwait(continueOnCapturedContext: true);
		}
		NewsText = NewsComposer.Compose(NorcomVersion.Current, news, notes, version);
	}

	private async Task LoadDiscordAsync()
	{
		string path = Path.Combine(NorcomPaths.GameDir, "discord-link.txt");
		string text = ((!File.Exists(path)) ? (await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/discord-link.txt").ConfigureAwait(continueOnCapturedContext: true)) : (await File.ReadAllTextAsync(path).ConfigureAwait(continueOnCapturedContext: true)));
		string packText = text;
		ApplyDiscord(packText);
	}

	private void ApplyDiscord(string? packText)
	{
		if (packText != null)
		{
			_packDiscordText = packText;
		}
		string path = Path.Combine(NorcomPaths.LauncherDir, "discord-invite.txt");
		string localFileText = (File.Exists(path) ? File.ReadAllText(path) : null);
		string path2 = Path.Combine(NorcomPaths.LauncherDir, "discord-link.txt");
		string legacyLauncherFileText = (File.Exists(path2) ? File.ReadAllText(path2) : null);
		DiscordUrl = DiscordLink.Choose(_packDiscordText, _settings.DiscordInviteUrl, Environment.GetEnvironmentVariable("NORCOM_DISCORD_INVITE"), localFileText, legacyLauncherFileText);
		DiscordEnabled = DiscordUrl != null;
	}

	public void SetCrashWebhook(string? url)
	{
		if (IsBusy || BridgeRuntime.HandoffScheduled) return;
		_settings.CrashWebhookUrl = url?.Trim() ?? "";
		_settings.Save(NorcomPaths.SettingsPath);
		RefreshCrashChannelStatus();
	}

	private void RefreshCrashChannelStatus()
	{
		string path = Path.Combine(NorcomPaths.LauncherDir, "crash-webhook.url");
		string fileText = (File.Exists(path) ? File.ReadAllText(path) : null);
		if (CrashIngest.Resolve(Environment.GetEnvironmentVariable("NORCOM_CRASH_INGEST"), "https://skaahanzi.tailefd7c9.ts.net/v1/crash") != null)
		{
			CrashChannelStatus = "Crash reports use the NORCOM receiver. The address is not shown.";
			return;
		}
		string text = CrashReportService.ResolveWebhook(Environment.GetEnvironmentVariable("NORCOM_CRASH_WEBHOOK"), _settings.CrashWebhookUrl, fileText);
		CrashChannelStatus = ((text == null) ? "Crash channel is not configured." : "Crash channel is configured. The address is not shown.");
	}

	public void DeliverQueuedReport(string reportPath)
	{
        StatusLine="A diagnostic report was saved locally. You can choose to send diagnostics from Settings.";
	}

	private string? CurrentWebhook()
	{
		string path = Path.Combine(NorcomPaths.LauncherDir, "crash-webhook.url");
		string fileText = (File.Exists(path) ? File.ReadAllText(path) : null);
		return CrashReportService.ResolveWebhook(Environment.GetEnvironmentVariable("NORCOM_CRASH_WEBHOOK"), _settings.CrashWebhookUrl, fileText);
	}

	private string? CurrentIngest()
	{
		return CrashIngest.Resolve(Environment.GetEnvironmentVariable("NORCOM_CRASH_INGEST"), "https://skaahanzi.tailefd7c9.ts.net/v1/crash");
	}

	private Task FlushCrashesAsync()
	{
		return CrashReportService.FlushAsync(_crashHttp, NorcomPaths.CrashOutbox, CurrentWebhook(), CurrentIngest(), WindowsVersion(), CrashIngest.ReadLogTail(NorcomPaths.LogPath), CancellationToken.None);
	}

	private Task<CrashReportService.Delivery> DeliverCrashAsync(string reportPath)
	{
		return CrashReportService.TryDeliverAsync(_crashHttp, reportPath, CurrentWebhook(), CurrentIngest(), WindowsVersion(), CrashIngest.ReadLogTail(NorcomPaths.LogPath), CancellationToken.None);
	}

	private static string WindowsVersion()
	{
		string oSDescription = RuntimeInformation.OSDescription;
		if (oSDescription.Length <= 40)
		{
			return oSDescription;
		}
		return oSDescription.Substring(0, 40);
	}

	private async Task<string?> TryGet(string url)
	{
		try
		{
			return await _metadataHttp.GetStringAsync(url).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("Fetch failed " + url + " " + ex.Message);
			return null;
		}
	}

	[RelayCommand]
	private void OpenPrismDownload()
	{
		Process.Start(new ProcessStartInfo("https://prismlauncher.org/download/")
		{
			UseShellExecute = true
		});
	}

	[RelayCommand(CanExecute = "CanSignIn")]
	private Task SignInBrowserAsync()
	{
		return RunSignInAsync(MicrosoftPrompt.SystemBrowser);
	}

	[RelayCommand(CanExecute = "CanSignIn")]
	private Task SignInDeviceAsync()
	{
		return RunSignInAsync(MicrosoftPrompt.DeviceCode);
	}

	private bool CanSignIn()
	{
		return !IsBusy && !QaGates.OfflineCheckRequested();
	}

	[RelayCommand(CanExecute = "CanCancelSignIn")]
	private void CancelSignIn()
	{
		_authCts?.Cancel();
	}

	private bool CanCancelSignIn()
	{
		return _authCts != null;
	}

	[RelayCommand(CanExecute = "CanSignOut")]
	private async Task SignOutAsync()
	{
		if (QaGates.OfflineCheckRequested()) return;
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		try
		{
			await _signIn.SignOutAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			SignedIn = false;
			AccountStatus = "Sign in with Microsoft in Settings. NORCOM does not read the official launcher account, and Play does not start without a verified Minecraft profile.";
			StatusLine = "Signed out of NORCOM. The official launcher account was left alone.";
		}
		catch (Exception ex)
		{
			AccountStatus = "Microsoft sign-in was refused. Play was not started.";
			LauncherLog.Write("Sign-out failed: " + ex.GetType().Name);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private bool CanSignOut()
	{
		return !IsBusy && !QaGates.OfflineCheckRequested() && (SignedIn
			|| File.Exists(Path.Combine(NorcomPaths.AuthDir, "session.bin"))
			|| File.Exists(Path.Combine(NorcomPaths.AuthDir, "msal.bin"))
			|| AccountStatus == AuthMessages.StorageUnavailable);
	}

	private async Task RunSignInAsync(MicrosoftPrompt prompt)
	{
		if (QaGates.OfflineCheckRequested()) return;
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		_authCts = new CancellationTokenSource();
		CancelSignInCommand.NotifyCanExecuteChanged();
		try
		{
			AuthResult authResult = await _signIn.SignInAsync(prompt, delegate(DeviceCodePrompt code)
			{
				string text = code.Display;
				Application current = Application.Current;
				if (current != null)
				{
					((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Func<string>)(() => DeviceCodeText = text), Array.Empty<object>());
				}
			}, _authCts.Token).ConfigureAwait(continueOnCapturedContext: true);
			if (authResult.Failure == AuthFailure.None && (object)authResult.Session != null)
			{
				SignedIn = true;
				AccountStatus = AuthMessages.SignedIn(authResult.Session.Name);
			}
			else
			{
				SignedIn = _signIn.RestoreStored().SignedIn;
				AccountStatus = ((authResult.Failure == AuthFailure.Cancelled && SignedIn) ? _signIn.RestoreStored().Message : authResult.Message);
			}
			StatusLine = authResult.Message;
		}
		catch (Exception ex)
		{
			AccountStatus = "Microsoft or Minecraft could not be reached. Play was not started.";
			StatusLine = AccountStatus;
			LauncherLog.Write("Sign-in failed: " + ex.GetType().Name);
		}
		finally
		{
			_authCts.Dispose();
			_authCts = null;
			DeviceCodeText = "";
			IsBusy = false;
			CancelSignInCommand.NotifyCanExecuteChanged();
		}
	}

	private void SaveToggles()
	{
        if(_loadingSettings)return;
		_settings.LauncherSounds = LauncherSounds;
		_settings.CloseToTray = CloseToTray;
		_settings.ReduceMotion = ReduceMotion;
		_settings.AnimationsPausedWhilePlaying = PauseWhilePlaying;
		_settings.Save(NorcomPaths.SettingsPath);
	}

	private void QueueProgress(TransferSnapshot shot, CancellationTokenSource pump)
	{
		Application current = Application.Current;
		if (current == null)
		{
			return;
		}
		((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			if (!ReferenceEquals(_pump,pump) || pump.IsCancellationRequested) return;
			ApplyProgress(shot);
		}, Array.Empty<object>());
	}

	private void ApplyProgress(TransferSnapshot snap)
	{
		if (snap.Epoch < _progressEpoch)
		{
			return;
		}
		_progressEpoch = snap.Epoch;
		if (snap.Fraction > 0.0)
		{
			ProgressFraction = snap.Fraction;
		}
		if (string.IsNullOrWhiteSpace(snap.Label))
		{
			if (string.Equals(ProgressLabel, "Pack ready", StringComparison.Ordinal))
			{
				ProgressLabel = "";
				ProgressDetail = "";
			}
			return;
		}
		ProgressLabel = snap.Label;
		ProgressDetail = TransferMeter.Format(snap);
	}

	private CancellationTokenSource StartPump(TransferMeter meter)
	{
		_pump?.Cancel();
		_progressEpoch = 0;
		CancellationTokenSource cts = new CancellationTokenSource();
		_pump = cts;
		Task.Run(async delegate
		{
			try
			{
				while (!cts.IsCancellationRequested)
				{
					QueueProgress(meter.Snapshot(),cts);
					await Task.Delay(100, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}, cts.Token);
		return cts;
	}

	private static void Open(string path)
	{
		Directory.CreateDirectory(path);
		Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
		{
			UseShellExecute = true
		});
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPageChanged(LauncherPage value)
	{
		OnPropertyChanged("IsHome");
		OnPropertyChanged("IsSettings");
		OnPropertyChanged("IsGraphics");
		OnPropertyChanged(nameof(IsMinecraftPage));
		OnPropertyChanged(nameof(IsZomboidPage));
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsBusyChanged(bool value)
	{
        OnPropertyChanged(nameof(PlayAvailability));
        OnPropertyChanged(nameof(CanManageClient));
		((IRelayCommand)PlayCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignInBrowserCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignInDeviceCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignOutCommand).NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGameRunningChanged(bool value)
	{
		((IRelayCommand)PlayCommand).NotifyCanExecuteChanged();
		OnPropertyChanged("PlayLabel");
        OnPropertyChanged(nameof(PlayAvailability));
        OnPropertyChanged(nameof(CanManageClient));
		OnPropertyChanged(nameof(CanStopSelectedGame));
		AnimationsPaused = value && PauseWhilePlaying;
		if (value)
		{
			StatusLine = "Minecraft is running. Glow animations are paused.";
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDiscordInviteChanged(string value)
	{
		_settings.DiscordInviteUrl = value?.Trim() ?? "";
        if(!_loadingSettings)_settings.Save(NorcomPaths.SettingsPath);
		ApplyDiscord(null);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSignedInChanged(bool value)
	{
		((IRelayCommand)PlayCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignOutCommand).NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnAccountStatusChanged(string value)
	{
		OnPropertyChanged("PlayAccountText");
		((IRelayCommand)SignOutCommand).NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedTierChanged(GraphicsTier value)
	{
		OnPropertyChanged("TierSummary");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnAutoOnChanged(bool value)
	{
		OnPropertyChanged("TierSummary");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnMemoryMbChanged(double value)
	{
		OnPropertyChanged("MemoryLabel");
        if(_loadingSettings || _syncingRam)return;
		RamBudget budget = GameBudget((int)value);
		if (budget.ChosenMb != (int)value)
		{
			_syncingRam = true;
			MemoryMb = budget.ChosenMb;
			_syncingRam = false;
		}
		if (IsZomboid)
		{
			_settings.ZomboidRamMb = budget.ChosenMb;
			_settings.ZomboidRamSpecified = true;
		}
		else
		{
			_settings.RamMb = budget.ChosenMb;
			_settings.RamSpecified = true;
		}
		_settings.Save(NorcomPaths.SettingsPath);
		NotifyRam();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnLauncherSoundsChanged(bool value)
	{
		SaveToggles();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCloseToTrayChanged(bool value)
	{
		SaveToggles();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnReduceMotionChanged(bool value)
	{
		SaveToggles();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPauseWhilePlayingChanged(bool value)
	{
		SaveToggles();
		AnimationsPaused = GameRunning && value;
	}
}
