using System.Windows;
using System.Windows.Controls;
using Norcom.Admin.ViewModels;

namespace Norcom.Admin.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void SecretChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpsViewModel ops && sender is PasswordBox box)
        {
            ops.SetPassword(box.Password);
        }
    }
}
