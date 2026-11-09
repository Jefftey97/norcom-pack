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
            ShortcutRepair.EnsureKnownShortcuts(BridgeRuntime.BootstrapPath);
            await vm.InitializeAsync();
            if(_closed)return;
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
        {var target=_vm.IsHome?(FrameworkElement)HomePanel:SettingsPanel;target.BeginAnimation(OpacityProperty,new DoubleAnimation(0.88,1,TimeSpan.FromMilliseconds(160)));}
    }
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
}
