using System.Windows;
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
        var window = new ShellWindow { DataContext = new ShellViewModel() };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance.Dispose();
        base.OnExit(e);
    }
}
