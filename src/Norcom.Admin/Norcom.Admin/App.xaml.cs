using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Norcom.Admin.Core;
using Norcom.Admin.Services;
using Norcom.Admin.ViewModels;
using Norcom.Admin.Views;

namespace Norcom.Admin;

public partial class App : Application
{
    private readonly SingleInstance _instance = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex && !IsXaml(ex))
            {
                LogOps(ex);
            }
        };
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

    private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsXaml(e.Exception))
        {
            return;
        }

        if (e.Exception is IOException or UnauthorizedAccessException)
        {
            LogOps(e.Exception);
            e.Handled = true;
        }
    }

    private static bool IsXaml(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is XamlParseException)
            {
                return true;
            }
        }

        return false;
    }

    private static void LogOps(Exception ex)
    {
        try
        {
            var path = AdminPaths.LogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + ex.GetType().Name + " " + ex.Message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
        }
        catch
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance.Dispose();
        base.OnExit(e);
    }
}
