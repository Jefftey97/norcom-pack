using System.Windows.Media.Animation;
using Norcom.Launcher.Core.Text;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Services;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;
public partial class ShellWindow : Window
{
    private DispatcherTimer _gameWatch;
    private DispatcherTimer _poll;
    private bool _closed;
    private bool _started;
    private ShellViewModel _vm;
    public ShellWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        var settings=LauncherSettings.Load(NorcomPaths.SettingsPath);
        var bounds=WindowSizing.Fit(settings.WindowW,settings.WindowH,SystemParameters.WorkArea.Width,SystemParameters.WorkArea.Height);
        MinWidth=bounds.MinWidth;MinHeight=bounds.MinHeight;Width=bounds.Width;Height=bounds.Height;
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(_started)return;_started=true;
        if(!await BridgeRuntime.WaitForReadyAsync() || _closed || DataContext is not ShellViewModel vm)return;
        _vm=vm;vm.PropertyChanged+=OnViewModelChanged;
        try
        {
            _gameWatch=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
            _gameWatch.Tick+=(_,_)=>vm.RefreshLaunchedGame();_gameWatch.Start();
            _poll=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
            _poll.Tick+=async(_,_)=>
            {
                if(!IsVisible || WindowState==WindowState.Minimized || vm.GameRunning || vm.IsBusy)return;
                await vm.RefreshServerCommand.ExecuteAsync(null);
                await vm.PollLivePinAsync(false);
            };
            _poll.Start();
            try{ShortcutRepair.EnsureKnownShortcuts(BridgeRuntime.BootstrapPath);}catch(Exception ex){LauncherLog.Write("shortcut repair "+ex.GetType().Name);}
            await vm.InitializeAsync();
            if(_closed)return;
            await vm.PollLivePinAsync(false);
        }
        catch(Exception ex){LauncherLog.Write("desktop initialization "+ex.GetType().Name);vm.StatusLine="Some checks are unavailable. You can retry from Home.";}
    }
    private async void OnActivated(object sender,EventArgs e)
    {
        if(!_started || _closed || DataContext is not ShellViewModel vm || vm.IsBusy || vm.GameRunning)return;
        var ready=BridgeRuntime.WaitForReadyAsync();
        if(!ready.IsCompletedSuccessfully || !ready.Result)return;
        try{await vm.RefreshServerCommand.ExecuteAsync(null);}catch(Exception ex){LauncherLog.Write("status refresh "+ex.GetType().Name);}
    }
    private void OnClosed(object sender,EventArgs e){_closed=true;_gameWatch?.Stop();_poll?.Stop();if(_vm!=null)_vm.PropertyChanged-=OnViewModelChanged;}
    private void OnViewModelChanged(object sender,PropertyChangedEventArgs e)
    {
        if(_vm.ReduceMotion || _vm.AnimationsPaused)
        {HomePanel.BeginAnimation(OpacityProperty,null);SettingsPanel.BeginAnimation(OpacityProperty,null);return;}
        if(e.PropertyName==nameof(ShellViewModel.Page))
        {FrameworkElement target=_vm.IsHome?HomePanel:_vm.IsMinecraftPage?MinecraftPanel:_vm.IsZomboidPage?ZomboidPanel:SettingsPanel;target.BeginAnimation(OpacityProperty,new DoubleAnimation(0.88,1,TimeSpan.FromMilliseconds(160)));}
    }
    // WindowStyle=None + WindowChrome maximizes to the full monitor, so the bottom of the
    // launcher (status bar, end of each page) ends up behind the taskbar. Clamp the maximized
    // size to the work area of the monitor the window is on.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if(System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle) is { } source)source.AddHook(MaximizeHook);
    }
    private static IntPtr MaximizeHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        const int WM_GETMINMAXINFO=0x0024;
        if(msg!=WM_GETMINMAXINFO)return IntPtr.Zero;
        var monitor=MonitorFromWindow(hwnd,2);
        var info=new MONITORINFO{cbSize=System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>()};
        if(monitor==IntPtr.Zero || !GetMonitorInfo(monitor,ref info))return IntPtr.Zero;
        var mmi=System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X=info.rcWork.Left-info.rcMonitor.Left;
        mmi.ptMaxPosition.Y=info.rcWork.Top-info.rcMonitor.Top;
        mmi.ptMaxSize.X=info.rcWork.Right-info.rcWork.Left;
        mmi.ptMaxSize.Y=info.rcWork.Bottom-info.rcWork.Top;
        System.Runtime.InteropServices.Marshal.StructureToPtr(mmi,lParam,true);
        return IntPtr.Zero;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct POINT{public int X;public int Y;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct RECT{public int Left;public int Top;public int Right;public int Bottom;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MINMAXINFO{public POINT ptReserved;public POINT ptMaxSize;public POINT ptMaxPosition;public POINT ptMinTrackSize;public POINT ptMaxTrackSize;}
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MONITORINFO{public int cbSize;public RECT rcMonitor;public RECT rcWork;public int dwFlags;}
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFO info);
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void OnClosing(object sender,System.ComponentModel.CancelEventArgs e)=>LauncherDialogs.DismissOwned(this);
}
