using System.Windows;

namespace Norcom.Admin.ViewModels;

public static class Ask
{
    public static bool Yes(string question) =>
        MessageBox.Show(question, "NORCOM Admin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void Warn(string message) =>
        MessageBox.Show(message, "NORCOM Admin", MessageBoxButton.OK, MessageBoxImage.Warning);
}
