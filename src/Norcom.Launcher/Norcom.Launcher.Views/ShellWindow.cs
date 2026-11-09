using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Services;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;

public class ShellWindow : Window, IComponentConnector
{
	private DispatcherTimer? _clock;

	private DispatcherTimer? _gameWatch;

	private DispatcherTimer? _pinPoll;

	private DispatcherTimer? _glideFallback;

	private TrayHost? _tray;

	private bool _glideDone;

	private static bool _period;

	internal Grid Root;

	internal TranslateTransform Glide;

	internal Button HomeNav;

	internal Button SettingsNav;

	private bool _contentLoaded;

	public ShellWindow()
	{
		InitializeComponent();
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected O, but got Unknown
		if (!await BridgeRuntime.WaitForReadyAsync()) return;
		_tray = new TrayHost((Window)(object)this);
		if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
		{
			ShortcutRepair.EnsureKnownShortcuts(BridgeRuntime.BootstrapPath);
		}
		PlayGlide();
		object dataContext = ((FrameworkElement)this).DataContext;
		ShellViewModel vm = dataContext as ShellViewModel;
		if (vm == null)
		{
			return;
		}
		PaintNav();
		((ObservableObject)vm).PropertyChanged += delegate(object? _, PropertyChangedEventArgs args)
		{
			bool flag;
			switch (args.PropertyName)
			{
			case "IsHome":
			case "IsSettings":
			case "Page":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				PaintNav();
			}
		};
		vm.InitializeAsync();
		if (PlayUpdateGate.ConsumeResume())
		{
			vm.StatusLine = "Launcher updated. Press Play when you are ready to join.";
		}
		_clock = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(8.0)
		};
		_clock.Tick += delegate
		{
			vm.AdvanceClock();
		};
		((ObservableObject)vm).PropertyChanged += delegate(object? _, PropertyChangedEventArgs args)
		{
			string propertyName = args.PropertyName;
			if ((propertyName == "AnimationsPaused" || propertyName == "ReduceMotion") ? true : false)
			{
				SyncClock();
			}
		};
		SyncClock();
		PlayChrome.Install((Window)(object)this, Root, vm);
		_gameWatch = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_gameWatch.Tick += delegate
		{
			vm.RefreshLaunchedGame();
		};
		_gameWatch.Start();
		_pinPoll = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(4.0)
		};
		_pinPoll.Tick += delegate
		{
			vm.PollLivePinAsync(force: false);
		};
		_pinPoll.Start();
		((Window)this).Activated += delegate
		{
			vm.PollLivePinAsync(force: true);
		};
		vm.PollLivePinAsync(force: false);
	}

	private void PaintNav()
	{
		if (((FrameworkElement)this).DataContext is ShellViewModel shellViewModel)
		{
			Paint(HomeNav, shellViewModel.IsHome);
			Paint(SettingsNav, shellViewModel.IsSettings);
		}
	}

	private static void Paint(Button button, bool selected)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		(string, string, string, bool) tuple = NavPaint.For(selected);
		((Control)button).Background = (Brush)(object)Brush(tuple.Item1);
		((Control)button).BorderBrush = (Brush)(object)Brush(tuple.Item2);
		((Control)button).Foreground = (Brush)(object)Brush(tuple.Item3);
		((Control)button).FontWeight = (tuple.Item4 ? FontWeights.SemiBold : FontWeights.Normal);
		NavChoice.SetIsCurrent((DependencyObject)(object)button, selected);
	}

	private static SolidColorBrush Brush(string hex)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		SolidColorBrush val = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		((Freezable)val).Freeze();
		return val;
	}

	private void SyncClock()
	{
		if (_clock != null && ((FrameworkElement)this).DataContext is ShellViewModel shellViewModel)
		{
			if (shellViewModel.ReduceMotion || shellViewModel.AnimationsPaused)
			{
				_clock.Stop();
				TryEndPeriod();
			}
			else
			{
				TryBeginPeriod();
				_clock.Start();
			}
		}
	}

	private void PlayGlide()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Expected O, but got Unknown
		//IL_0064: Expected O, but got Unknown
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Expected O, but got Unknown
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Expected O, but got Unknown
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Expected O, but got Unknown
		try
		{
			if (((FrameworkElement)this).DataContext is ShellViewModel { ReduceMotion: not false })
			{
				FinishGlide();
				return;
			}
			Storyboard val = new Storyboard();
			DoubleAnimation val2 = new DoubleAnimation(22.0, 0.0, TimeSpan.FromMilliseconds(520.0))
			{
				EasingFunction = (IEasingFunction)new CubicEase
				{
					EasingMode = (EasingMode)1
				}
			};
			Storyboard.SetTarget((DependencyObject)(object)val2, (DependencyObject)(object)Glide);
			Storyboard.SetTargetProperty((DependencyObject)(object)val2, new PropertyPath((object)TranslateTransform.YProperty));
			DoubleAnimation val3 = new DoubleAnimation(1.0, 1.0, TimeSpan.FromMilliseconds(420.0));
			Storyboard.SetTarget((DependencyObject)(object)val3, (DependencyObject)(object)Root);
			Storyboard.SetTargetProperty((DependencyObject)(object)val3, new PropertyPath((object)UIElement.OpacityProperty));
			((TimelineGroup)val).Children.Add((Timeline)(object)val2);
			((TimelineGroup)val).Children.Add((Timeline)(object)val3);
			((Timeline)val).Completed += delegate
			{
				FinishGlide();
			};
			val.Begin();
			_glideFallback = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(900.0)
			};
			_glideFallback.Tick += delegate
			{
				FinishGlide();
			};
			_glideFallback.Start();
		}
		catch
		{
			FinishGlide();
		}
	}

	private void FinishGlide()
	{
		if (!_glideDone)
		{
			_glideDone = true;
			DispatcherTimer? glideFallback = _glideFallback;
			if (glideFallback != null)
			{
				glideFallback.Stop();
			}
			((UIElement)Root).Opacity = 1.0;
			Glide.Y = 0.0;
		}
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		DispatcherTimer? clock = _clock;
		if (clock != null)
		{
			clock.Stop();
		}
		DispatcherTimer? gameWatch = _gameWatch;
		if (gameWatch != null)
		{
			gameWatch.Stop();
		}
		DispatcherTimer? pinPoll = _pinPoll;
		if (pinPoll != null)
		{
			pinPoll.Stop();
		}
		TryEndPeriod();
		_tray?.Dispose();
		Application current = Application.Current;
		if (current != null)
		{
			current.Shutdown();
		}
	}

	private void MinimizeClick(object sender, RoutedEventArgs e)
	{
		((Window)this).WindowState = (WindowState)1;
	}

	private void MaxClick(object sender, RoutedEventArgs e)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Invalid comparison between Unknown and I4
		((Window)this).WindowState = (WindowState)(((int)((Window)this).WindowState != 2) ? 2 : 0);
	}

	private void CloseClick(object sender, RoutedEventArgs e)
	{
		((Window)this).Close();
	}

	private static void TryBeginPeriod()
	{
		if (_period)
		{
			return;
		}
		try
		{
			if (timeBeginPeriod(1u) == 0)
			{
				_period = true;
			}
		}
		catch
		{
		}
	}

	private static void TryEndPeriod()
	{
		if (_period)
		{
			try
			{
				timeEndPeriod(1u);
			}
			catch
			{
			}
			_period = false;
		}
	}

	[DllImport("winmm.dll")]
	private static extern uint timeBeginPeriod(uint period);

	[DllImport("winmm.dll")]
	private static extern uint timeEndPeriod(uint period);

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/NorcomLauncher;V2.0.1.0;component/views/shellwindow.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Expected O, but got Unknown
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected O, but got Unknown
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Expected O, but got Unknown
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected O, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			((FrameworkElement)(ShellWindow)target).Loaded += new RoutedEventHandler(OnLoaded);
			((Window)(ShellWindow)target).Closing += OnClosing;
			break;
		case 2:
			Root = (Grid)target;
			break;
		case 3:
			Glide = (TranslateTransform)target;
			break;
		case 4:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(MinimizeClick);
			break;
		case 5:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(MaxClick);
			break;
		case 6:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(CloseClick);
			break;
		case 7:
			HomeNav = (Button)target;
			break;
		case 8:
			SettingsNav = (Button)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
