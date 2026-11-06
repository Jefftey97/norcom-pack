using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Norcom.Admin.ViewModels;

namespace Norcom.Admin.Views;

public partial class ShellWindow : Window
{
    private DispatcherTimer? _poll;
    private bool _fullscreen;

    public ShellWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm)
        {
            return;
        }

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => vm.Poll();
        _poll.Start();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fullscreen)
        {
            LeaveFullscreen();
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
        }
    }

    private void FullClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void MinClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaxClick(sender, e);
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
        }
    }

    private void ToggleFullscreen()
    {
        if (_fullscreen)
        {
            LeaveFullscreen();
            return;
        }

        _fullscreen = true;
        TitleBar.Visibility = Visibility.Collapsed;
        WindowState = WindowState.Maximized;
    }

    private void LeaveFullscreen()
    {
        _fullscreen = false;
        TitleBar.Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
    }
}
