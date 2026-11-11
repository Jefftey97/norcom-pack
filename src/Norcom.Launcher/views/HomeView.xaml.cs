using Norcom.Launcher.Core.Text;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;
public partial class HomeView : UserControl
{
    private ShellViewModel _hooked;
    public HomeView()
    {
        InitializeComponent();
        Loaded += delegate { SyncPasswordBox(); };
        DataContextChanged += delegate
        {
            if(_hooked!=null)_hooked.PropertyChanged -= OnViewModelChanged;
            _hooked=DataContext as ShellViewModel;
            if(_hooked!=null)_hooked.PropertyChanged += OnViewModelChanged;
            SyncPasswordBox();
        };
    }
    private void OnViewModelChanged(object sender, PropertyChangedEventArgs args)
    {
        if(args.PropertyName == nameof(ShellViewModel.PasswordEnabled) || args.PropertyName == nameof(ShellViewModel.SelectedGameId))SyncPasswordBox();
    }
    private void SyncPasswordBox()
    {
        if(ZomboidPassword==null)return;
        string saved=DataContext is ShellViewModel vm && vm.PasswordEnabled && ZomboidPasswordStore.Exists(NorcomPaths.NorcomHome)?ZomboidPasswordStore.Load(NorcomPaths.NorcomHome):"";
        if(ZomboidPassword.Password!=saved)ZomboidPassword.Password=saved;
    }
    private void OnSizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(Overview==null)return;
        var narrow=ActualWidth<940;
        Overview.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(2,GridUnitType.Star);
        Grid.SetRow(ServerCard,narrow?1:0);Grid.SetColumn(ServerCard,narrow?0:1);
        PlayCard.Margin=new Thickness(0,0,narrow?0:18,18);
    }
    private void StopClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is ShellViewModel vm && LauncherDialogs.Confirm("Close the "+vm.StopGameTitle+" game started by NORCOM? Save your world first.","Stop game"))vm.StopLaunchedGame();
    }
    private void ZomboidPasswordChanged(object sender,RoutedEventArgs e)
    {
        if(DataContext is ShellViewModel vm && sender is PasswordBox box)vm.SetPendingZomboidPassword(box.Password);
    }
    private async void CheckPackClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is not ShellViewModel vm)return;
        if(!vm.ChecksPackOnPlay){vm.StatusLine=vm.PackCheckNote;return;}
        try{await vm.FixPinAsync();}catch(Exception ex){LauncherLog.Write("pack check "+ex.GetType().Name);vm.StatusLine="Pack check unavailable. Try again when your connection is ready.";}
    }
}
