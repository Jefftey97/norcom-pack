using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
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
	private readonly HttpClient _http = new HttpClient
	{
		Timeout = TimeSpan.FromMinutes(30.0)
	};

	private readonly HttpClient _crashHttp = new HttpClient(new HttpClientHandler
	{
		AllowAutoRedirect = false
	})
	{
		Timeout = TimeSpan.FromSeconds(12.0)
	};

	private readonly HttpClient _authHttp = MinecraftServicesAuth.CreateHttp();

	private readonly GraphicsService _graphics = new GraphicsService();

	private readonly LauncherSettings _settings;

	private readonly NorcomSignIn _signIn;

	private CancellationTokenSource? _pump;

	private CancellationTokenSource? _authCts;

	private string? _packDiscordText;

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

	public bool CanPlay
	{
		get
		{
			if (!IsBusy)
			{
				return !GameRunning;
			}
			return false;
		}
	}

	public string PlayAccountText => AccountStatus;

	public string MemoryLabel => $"{(int)MemoryMb} MB";

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

	public ShellViewModel()
	{
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(NorcomVersion.UserAgent);
		_settings = LauncherSettings.Load(NorcomPaths.SettingsPath);
		Directory.CreateDirectory(NorcomPaths.GameDir);
		Directory.CreateDirectory(NorcomPaths.LauncherDir);
		MemoryMb = _settings.RamMb;
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
		GameRunning = StartupHeal.GameLockAlive(NorcomPaths.GameLockPath);
		AnimationsPaused = GameRunning && PauseWhilePlaying;
		PlayAccount.Install(this);
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
	}

	public async Task InitializeAsync()
	{
		FlushCrashesAsync();
		await Task.WhenAll(RefreshServerAsync(), LoadNewsAsync(), LoadDiscordAsync()).ConfigureAwait(continueOnCapturedContext: true);
	}

	public async Task<bool> ApplyUpdateIfNeededAsync(Action<string, double> report, CancellationToken ct)
	{
		_ = 1;
		try
		{
			UpdateReport.ClearRejected(NorcomPaths.LauncherDir);
			report("Checking for a launcher update", 0.05);
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(12.0));
			SelfUpdateService updates = new SelfUpdateService(_http);
			LauncherManifest manifest = await updates.FetchManifestAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: true);
			if (manifest == null)
			{
				UpdateReport.Write(NorcomPaths.LauncherDir, "no-manifest");
				report("No update manifest. Continuing.", 1.0);
				return false;
			}
			if (UpdateReport.SameSha(UpdateReport.ReadAppliedSha(NorcomPaths.LauncherDir), manifest.Sha256))
			{
				NoteUpdate("rejected already-applied");
				report("rejected already-applied", 1.0);
				return false;
			}
			if (SelfUpdateService.Decide(NorcomVersion.Current, manifest, updates.UsedPrivateManifest) == UpdateKind.None)
			{
				string text = (SelfUpdateService.IsAllowedPackageUrl(manifest.ZipUrl, updates.UsedPrivateManifest) ? "up-to-date" : "rejected not-https");
				NoteUpdate(text);
				report("Launcher " + NorcomVersion.Current + ". Published manifest is " + manifest.Version + ".", 1.0);
				StatusLine = "Launcher " + NorcomVersion.Current + ". Published manifest is " + manifest.Version + ". " + text;
				return false;
			}
			report("Downloading " + manifest.Version, 0.1);
			TransferMeter meter = new TransferMeter();
			string zipPath = await updates.DownloadVerifiedAsync(manifest, meter, ct).ConfigureAwait(continueOnCapturedContext: true);
			if (SelfUpdateService.Inspect(zipPath) != UpdatePackageKind.NativeExe)
			{
				NoteUpdate("rejected legacy-package");
				report("rejected legacy-package", 1.0);
				return false;
			}
			string text2 = SelfUpdateService.FindNativeExe(zipPath);
			if (text2 == null)
			{
				NoteUpdate("rejected missing-exe");
				report("rejected missing-exe", 1.0);
				return false;
			}
			string payloadVersion = SelfUpdateService.ReadPayloadVersion(text2);
			string exeFileVersion = NorcomVersion.TryReadExeVersion(text2);
			string text3 = UpdateReport.Rejection(NorcomVersion.Current, manifest.Version, payloadVersion, exeFileVersion, null, manifest.Sha256);
			if (text3 != null)
			{
				NoteUpdate(text3);
				report(text3, 1.0);
				return false;
			}
			string processPath = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(processPath))
			{
				NoteUpdate("rejected missing-exe");
				report("Cannot replace the running exe because its path is unknown.", 1.0);
				return false;
			}
			report("Installing " + manifest.Version, 0.95);
			SelfUpdateService.ScheduleReplace(text2, processPath, Environment.ProcessId, manifest.Version, manifest.Sha256);
			return true;
		}
		catch (UpdateRejectedException ex)
		{
			NoteUpdate(ex.Token);
			report(ex.Token, 1.0);
			return false;
		}
		catch (Exception ex2)
		{
			NoteUpdate("failed " + ex2.GetType().Name);
			StatusLine = "Update failed. " + Redactor.Clean(ex2.Message);
			report(StatusLine, 1.0);
			return false;
		}
	}

	private void NoteUpdate(string token)
	{
		UpdateReport.Write(NorcomPaths.LauncherDir, token);
		StatusLine = "Launcher " + NorcomVersion.Current + ". " + token;
	}

	[RelayCommand(CanExecute = "CanPlay")]
	private async Task PlayAsync()
	{
		IsBusy = true;
		ProgressFraction = 0.02;
		ProgressLabel = "Preparing NORCOM";
		TransferMeter meter = new TransferMeter();
		CancellationTokenSource pump = StartPump(meter);
		try
		{
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
			if (LauncherSounds)
			{
				UiSound.Play();
			}
			GameRunning = true;
			PrismPreparation prismPreparation2 = await PrismPlay.RunAsync(prismPreparation, new PrismProcess(), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			GameRunning = false;
			ProgressFraction = 1.0;
			ProgressLabel = (prismPreparation2.CanStart ? "Prism closed" : "Prism did not start Minecraft");
			ProgressDetail = "";
			AccountStatus = prismPreparation2.Message;
			StatusLine = prismPreparation2.Message;
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
			pump.Cancel();
			IsBusy = false;
		}
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

	private void ApplyResolved(GraphicsTier tier, bool auto, string why)
	{
		_graphics.Apply(NorcomPaths.GameDir, tier);
		IReadOnlyList<string> readOnlyList = _graphics.ClearShaderCache(NorcomPaths.GameDir);
		_settings.GraphicsTier = tier;
		_settings.GraphicsAuto = auto;
		_settings.Save(NorcomPaths.SettingsPath);
		SelectedTier = tier;
		AutoOn = auto;
		StatusLine = (auto ? "Auto chose " : "") + GraphicsPresets.TierLabel(tier) + " applied. " + why + " Shader cache cleared (" + readOnlyList.Count + "). Keybinds and servers were left alone.";
	}

	[RelayCommand]
	private async Task FixVisualsAsync()
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		TransferMeter meter = new TransferMeter();
		CancellationTokenSource pump = StartPump(meter);
		try
		{
			GraphicsTier tier = _settings.GraphicsTier;
			if (_settings.GraphicsAuto)
			{
				PcHardware pcHardware = new WmiPcProbe().TryRead();
				tier = (((object)pcHardware == null) ? GraphicsTier.High : GraphicsAuto.Choose(pcHardware));
				_settings.GraphicsTier = tier;
				_settings.Save(NorcomPaths.SettingsPath);
			}
			string backup = _graphics.BackupAndReset(NorcomPaths.GameDir, tier);
			await KnownVisualAssets.EnsureAsync(_http, NorcomPaths.GameDir, meter, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			IReadOnlyList<string> readOnlyList = _graphics.ClearShaderCache(NorcomPaths.GameDir);
			SelectedTier = tier;
			ProgressFraction = 1.0;
			ProgressLabel = "Visuals repaired";
			StatusLine = "Reset to " + GraphicsPresets.TierLabel(tier) + ". Cache entries removed: " + readOnlyList.Count + ". Backup: " + backup;
		}
		catch (Exception ex)
		{
			StatusLine = "Fix Visuals stopped. " + Redactor.Clean(ex.Message);
			ProgressLabel = "Fix Visuals didn't finish";
		}
		finally
		{
			pump.Cancel();
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task RepairAsync()
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		TransferMeter meter = new TransferMeter();
		CancellationTokenSource pump = StartPump(meter);
		try
		{
			StartupHeal.ClearStale(NorcomPaths.LauncherDir, NorcomPaths.GameDir);
			GameRunning = StartupHeal.GameLockAlive(NorcomPaths.GameLockPath);
			await new PackSyncService(_http).SyncAsync(NorcomPaths.GameDir, NorcomPaths.ManagedManifestPath, NorcomPaths.PackwizCacheDir, "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml", meter, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true);
			ProgressFraction = 1.0;
			ProgressLabel = "Repair finished";
			StatusLine = RepairTrace.SuccessLine();
		}
		catch (Exception ex)
		{
			StatusLine = "Repair stopped. " + Redactor.Clean(ex.Message);
		}
		finally
		{
			pump.Cancel();
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task CheckUpdatesAsync()
	{
		if (await ApplyUpdateIfNeededAsync(delegate
		{
		}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: true))
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
		Open(NorcomPaths.GameDir);
	}

	[RelayCommand]
	private void OpenScreenshotsFolder()
	{
		Directory.CreateDirectory(NorcomPaths.ScreenshotsDir);
		Open(NorcomPaths.ScreenshotsDir);
	}

	[RelayCommand]
	private async Task SendDiagnostic()
	{
		string reportPath = CrashReportService.Queue("manual", "Manual diagnostic from Settings", null);
		MessageBox.Show((await DeliverCrashAsync(reportPath).ConfigureAwait(continueOnCapturedContext: true)).UserText, "NORCOM", (MessageBoxButton)0, (MessageBoxImage)64);
	}

	private Task RefreshServerAsync()
	{
		HomeStatus homeStatus = HomeStatusClient.Read();
		IsOnline = homeStatus.ShowOnline;
		ServerHeadline = homeStatus.Headline;
		PlayersText = homeStatus.PlayersText;
		Motd = homeStatus.Detail;
		LatencyText = homeStatus.Source;
		return Task.CompletedTask;
	}

	private async Task LoadNewsAsync()
	{
		string news = await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/news.md").ConfigureAwait(continueOnCapturedContext: true);
		string notes = await TryGet("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/pack-notes.md").ConfigureAwait(continueOnCapturedContext: true);
		string text = await TryGet("https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml").ConfigureAwait(continueOnCapturedContext: true);
		string version = ((text == null) ? null : TomlLite.Parse(text).Keys.GetValueOrDefault("version"));
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
		DeliverCrashAsync(reportPath);
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
			return await _http.GetStringAsync(url).ConfigureAwait(continueOnCapturedContext: true);
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
		return !IsBusy;
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
		if (SignedIn)
		{
			return !IsBusy;
		}
		return false;
	}

	private async Task RunSignInAsync(MicrosoftPrompt prompt)
	{
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
		_settings.LauncherSounds = LauncherSounds;
		_settings.CloseToTray = CloseToTray;
		_settings.ReduceMotion = ReduceMotion;
		_settings.AnimationsPausedWhilePlaying = PauseWhilePlaying;
		_settings.Save(NorcomPaths.SettingsPath);
	}

	private CancellationTokenSource StartPump(TransferMeter meter)
	{
		_pump?.Cancel();
		CancellationTokenSource cts = new CancellationTokenSource();
		_pump = cts;
		Task.Run(async delegate
		{
			try
			{
				while (!cts.IsCancellationRequested)
				{
					TransferSnapshot snap = meter.Snapshot();
					Application current = Application.Current;
					if (current != null)
					{
						((DispatcherObject)current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
						{
							if (snap.Fraction > 0.0)
							{
								ProgressFraction = snap.Fraction;
							}
							if (!string.IsNullOrWhiteSpace(snap.Label))
							{
								ProgressLabel = snap.Label;
							}
							ProgressDetail = TransferMeter.Format(snap);
						}, Array.Empty<object>());
					}
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
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsBusyChanged(bool value)
	{
		((IRelayCommand)PlayCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignInBrowserCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignInDeviceCommand).NotifyCanExecuteChanged();
		((IRelayCommand)SignOutCommand).NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGameRunningChanged(bool value)
	{
		((IRelayCommand)PlayCommand).NotifyCanExecuteChanged();
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
		_settings.Save(NorcomPaths.SettingsPath);
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
		_settings.RamMb = (int)value;
		_settings.Save(NorcomPaths.SettingsPath);
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
