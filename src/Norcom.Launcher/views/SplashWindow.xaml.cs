using System.Windows;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
namespace Norcom.Launcher.Views;
public partial class SplashWindow : Window
{
    public SplashWindow(){InitializeComponent();OpeningProgress.IsIndeterminate=!LauncherSettings.Load(NorcomPaths.SettingsPath).ReduceMotion;OpeningProgress.Value=50;}
}
