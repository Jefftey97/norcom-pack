using System.IO;
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
            Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE","1");
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
            vm.Page=LauncherPage.Minecraft;Pump();
            var minecraft=Assert.IsType<GraphicsMenuView>(window.FindName("MinecraftPanel"));
            Assert.Equal(Visibility.Visible,minecraft.Visibility);
            var card=Assert.IsType<RamCard>(minecraft.FindName("MemoryCard"));
            var memory=Assert.IsType<Slider>(card.FindName("MemoryControl"));
            Assert.Equal(vm.RamFloorMb,memory.Minimum);Assert.Equal(vm.RamSafeMaxMb,memory.Maximum);
            double pick=Math.Min(7168,memory.Maximum);if(pick<memory.Minimum)pick=memory.Minimum;
            memory.Value=pick;Pump();Assert.Equal(pick,vm.MemoryMb);
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
            var motion=Assert.IsType<CheckBox>(settings.FindName("ReduceMotionControl"));
            var desired=!vm.ReduceMotion;motion.IsChecked=desired;Pump();Assert.Equal(desired,vm.ReduceMotion);
            var repair=Assert.IsType<Button>(settings.FindName("RepairAction"));Assert.Same(vm.RepairCommand,repair.Command);
            window.Width=600;window.Height=500;window.Measure(new Size(600,500));window.Arrange(new Rect(0,0,600,500));Pump();
            Assert.Equal(Visibility.Visible,settings.Visibility);
            var scaled=new SettingsView{DataContext=vm,LayoutTransform=new System.Windows.Media.ScaleTransform(2,2)};
            scaled.Measure(new Size(1180,800));scaled.Arrange(new Rect(0,0,1180,800));Pump();
            Assert.InRange(scaled.ActualWidth,100,600);
            VerifyProgressCompletion();
            window.Close();
        });
    }
    private static void VerifyProgressCompletion()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var type=typeof(ShellViewModel);
        var queue=type.GetMethod("QueueProgress",flags)!;
        var start=type.GetMethod("StartPump",flags)!;
        var pump=type.GetField("_pump",flags)!;
        var epoch=type.GetField("_progressEpoch",flags)!;
        // Keep the real compiled command and graphics-reset path; replace only asset I/O.
        var vm=new ShellViewModel(new ControlledGameLookup(),(_,_,meter,_)=>
        {
            meter.SetPhase("Checking Alacrity.zip",0.99,0.01);
            return System.Threading.Tasks.Task.CompletedTask;
        });
        Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE","0");
        try
        {
            vm.ProgressDetail="Checking Alacrity.zip";
            var repair=vm.FixVisualsCommand.ExecuteAsync(null);
            Assert.True(repair.IsCompleted,"Injected repair must be synchronous and offline");
            repair.GetAwaiter().GetResult();Pump();
            Assert.Equal("Visuals repaired",vm.ProgressLabel);Assert.Equal("",vm.ProgressDetail);Assert.False(vm.IsBusy);
            var cancelled=new CancellationTokenSource();pump.SetValue(vm,cancelled);
            queue.Invoke(vm,new object[]{new Norcom.Launcher.Core.Services.TransferSnapshot("Checking Alacrity.zip",0.99,0,null,0,null,10),cancelled});
            cancelled.Cancel();vm.ProgressLabel="Visuals repaired";vm.ProgressDetail="";Pump();
            Assert.Equal("Visuals repaired",vm.ProgressLabel);Assert.Equal("",vm.ProgressDetail);
            // A queued callback from a replaced but uncancelled pump is also stale.
            var previous=new CancellationTokenSource();pump.SetValue(vm,previous);
            queue.Invoke(vm,new object[]{new Norcom.Launcher.Core.Services.TransferSnapshot("Old operation",0.99,0,null,0,null,10),previous});
            var replacement=new CancellationTokenSource();pump.SetValue(vm,replacement);Pump();
            Assert.Equal("Visuals repaired",vm.ProgressLabel);previous.Cancel();replacement.Cancel();
            epoch.SetValue(vm,10L);
            var meter=new Norcom.Launcher.Core.Services.TransferMeter();meter.SetPhase("New operation",0.1,0.1);
            var fresh=(CancellationTokenSource)start.Invoke(vm,new object[]{meter})!;
            queue.Invoke(vm,new object[]{meter.Snapshot(),fresh});Pump();fresh.Cancel();
            Assert.Equal("New operation",vm.ProgressLabel);
            var failed=new ShellViewModel(new ControlledGameLookup(),(_,_,_,_)=>throw new IOException("Test failure"));
            failed.ProgressDetail="Checking Alacrity.zip";
            failed.FixVisualsCommand.ExecuteAsync(null).GetAwaiter().GetResult();Pump();
            Assert.Equal("Fix Visuals didn't finish",failed.ProgressLabel);Assert.Equal("",failed.ProgressDetail);Assert.False(failed.IsBusy);
        }
        finally{Environment.SetEnvironmentVariable("NORCOM_QA_OFFLINE","1");((CancellationTokenSource?)pump.GetValue(vm))?.Cancel();}
    }
    private sealed class ControlledGameLookup : Norcom.Launcher.Core.Services.IGameProcessLookup
    {
        public int? Pid {get;set;}
        public int? TryGameProcessId(int? parent)=>Pid;
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
    private static void OnSta(Action action)=>UiTestHost.Run(action);
}
