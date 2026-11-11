using Norcom.Launcher.Core.Text;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;
public partial class SettingsView : UserControl
{
    public SettingsView()=>InitializeComponent();
    private void OpenLogsClick(object sender,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo(NorcomPaths.LauncherDir){UseShellExecute=true});}
        catch(Exception ex){SetStatus("Could not open the log folder. "+ex.GetType().Name);}
    }
    private void CopyDiagnosticsClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is not ShellViewModel vm)return;
        try{Clipboard.SetText($"NORCOM Launcher {NorcomVersion.Current}\nWindows: {Environment.OSVersion.Version}\nServer: {vm.ServerHeadline}\nPack: {vm.PinStatus}\nActivity: {vm.ProgressLabel}");SetStatus("Diagnostic summary copied. Review it before sharing.");}
        catch(Exception ex){SetStatus("Clipboard is unavailable. "+ex.GetType().Name);}
    }
    private async void SendDiagnosticsClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is not ShellViewModel vm)return;
        if(!LauncherDialogs.Confirm("Send a redacted launcher report and recent launcher log to the configured NORCOM support receiver? Account files are not included.","Send diagnostics"))return;
        try{await vm.SendDiagnosticCommand.ExecuteAsync(null);}catch(Exception ex){LauncherLog.Write("manual diagnostics "+ex.GetType().Name);SetStatus("Report delivery could not be confirmed. The local report was kept.");}
    }
    private void SetStatus(string text){if(DataContext is ShellViewModel vm)vm.StatusLine=text;}
}
