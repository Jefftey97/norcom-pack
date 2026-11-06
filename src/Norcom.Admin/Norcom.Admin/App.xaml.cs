using System.Windows;
using System.Windows.Threading;
using Norcom.Admin.Services;
using Norcom.Admin.ViewModels;
using Norcom.Admin.Views;

namespace Norcom.Admin;

public partial class App : Application
{
    private readonly SingleInstance _instance = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!_instance.Acquire())
        {
            Shutdown(0);
            return;
        }

        DesktopShortcut.Ensure();
        var shell = new ShellViewModel();
        var window = new ShellWindow { DataContext = shell };
        MainWindow = window;
        StartupFrame.Present(window);
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(shell.Warmup));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance.Dispose();
        base.OnExit(e);
    }
}
