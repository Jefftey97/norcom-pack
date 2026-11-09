using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Norcom.Launcher.ViewModels;
using Norcom.Launcher.Views;
using Xunit;

[assembly:CollectionBehavior(DisableTestParallelization=true)]
namespace Norcom.Launcher.UiTests;
public class DesktopContractTests
{
    [Fact] public void CompiledDesktopPreservesSafeNavigationAndSettingsBindings()
    {
        OnSta(()=>
        {
            var app=new Norcom.Launcher.App();app.InitializeComponent();
            var settingsPath=Norcom.Launcher.Core.NorcomPaths.SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath,"""{"RamMb":5632,"LauncherSounds":true,"ReduceMotion":true,"AnimationsPausedWhilePlaying":false,"custom":{"keep":true}}""");
            var originalSettings=File.ReadAllBytes(settingsPath);
            var lookup=new ControlledGameLookup();
            var window=new ShellWindow();var vm=new ShellViewModel(lookup);window.DataContext=vm;
            Assert.Equal(originalSettings,File.ReadAllBytes(settingsPath));
            Assert.True(vm.ReduceMotion);Assert.True(vm.LauncherSounds);Assert.False(vm.PauseWhilePlaying);
            window.Measure(new Size(1180,760));window.Arrange(new Rect(0,0,1180,760));Pump();
            var home=Assert.IsType<HomeView>(window.FindName("HomePanel"));
            var play=Assert.IsType<Button>(home.FindName("PlayAction"));
            Assert.Same(vm.PlayCommand,play.Command);Assert.False(play.IsEnabled);
            Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE","0");
            try
            {
                vm.IsBusy=true;Pump();Assert.False(play.IsEnabled);
                vm.IsBusy=false;Pump();Assert.True(play.IsEnabled);
                vm.GameRunning=true;Pump();Assert.False(play.IsEnabled);
            }
            finally{Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE","1");vm.GameRunning=false;}
            vm.Page=LauncherPage.Settings;Pump();
            Assert.Equal(Visibility.Collapsed,home.Visibility);
            var settings=Assert.IsType<SettingsView>(window.FindName("SettingsPanel"));
            Assert.Equal(Visibility.Visible,settings.Visibility);
            Assert.Empty(Directory.GetFiles(Norcom.Launcher.Core.NorcomPaths.GameDir,"*",SearchOption.AllDirectories));
            vm.ApplyTierCommand.Execute(Norcom.Launcher.Core.Models.GraphicsTier.Low);
            Assert.Empty(Directory.GetFiles(Norcom.Launcher.Core.NorcomPaths.GameDir,"*",SearchOption.AllDirectories));
            using(var discovered=System.Diagnostics.Process.GetCurrentProcess())
            {
                lookup.Pid=discovered.Id;vm.IsBusy=true;vm.RefreshLaunchedGame();
                Assert.True(vm.GameRunning);Assert.Equal(discovered.Id,Norcom.Launcher.Core.Services.StartupHeal.ReadGamePid(Norcom.Launcher.Core.NorcomPaths.GameLockPath));
                Assert.Contains("processStartedUtcTicks=",File.ReadAllText(Norcom.Launcher.Core.NorcomPaths.GameLockPath));
                File.Delete(Norcom.Launcher.Core.NorcomPaths.GameLockPath);lookup.Pid=null;vm.IsBusy=false;vm.GameRunning=false;
            }
            using(var current=System.Diagnostics.Process.GetCurrentProcess())
            {
                var gameLock=Norcom.Launcher.Core.NorcomPaths.GameLockPath;
                File.WriteAllText(gameLock,$"pid={current.Id}\nprocessStartedUtcTicks=1\n");
                vm.RefreshLaunchedGame();
                Assert.False(vm.GameRunning);Assert.False(File.Exists(gameLock));Assert.False(current.HasExited);
            }
            var memory=Assert.IsType<Slider>(settings.FindName("MemoryControl"));
            Assert.Equal(2048,memory.Minimum);Assert.Equal(16384,memory.Maximum);
            memory.Value=7168;Pump();Assert.Equal(7168,vm.MemoryMb);
            var motion=Assert.IsType<CheckBox>(settings.FindName("ReduceMotionControl"));
            var desired=!vm.ReduceMotion;motion.IsChecked=desired;Pump();Assert.Equal(desired,vm.ReduceMotion);
            var repair=Assert.IsType<Button>(settings.FindName("RepairAction"));Assert.Same(vm.RepairCommand,repair.Command);
            window.Width=600;window.Height=500;window.Measure(new Size(600,500));window.Arrange(new Rect(0,0,600,500));Pump();
            Assert.Equal(Visibility.Visible,settings.Visibility);
            var scaled=new SettingsView{DataContext=vm,LayoutTransform=new System.Windows.Media.ScaleTransform(2,2)};
            scaled.Measure(new Size(1180,800));scaled.Arrange(new Rect(0,0,1180,800));Pump();
            Assert.InRange(scaled.ActualWidth,100,600);
            window.Close();app.Shutdown();
        });
    }
    private sealed class ControlledGameLookup : Norcom.Launcher.Core.Services.IGameProcessLookup
    {
        public int? Pid {get;set;}
        public int? TryGameProcessId(int? parent)=>Pid;
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
    private static void OnSta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(30)),"STA UI check timed out");
        if(error!=null)ExceptionDispatchInfo.Capture(error).Throw();
    }
}
