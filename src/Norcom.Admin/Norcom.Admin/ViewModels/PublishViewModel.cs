using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class PublishViewModel : ObservableObject
{
    private readonly AdminSettings _settings;
    private readonly SecretRedactor _redactor;
    private readonly Action _save;
    private readonly PackPublisher _publisher;
    private readonly Func<string> _serverPath;

    public PublishViewModel(AdminSettings settings, SecretRedactor redactor, Action save, Func<string> serverPath)
    {
        _settings = settings;
        _redactor = redactor;
        _save = save;
        _serverPath = serverPath;
        _publisher = new PackPublisher(new ProcessCommandRunner(), redactor);
        PackPath = settings.PackPath;
    }

    [ObservableProperty] private string packPath = "";
    [ObservableProperty] private string confirmText = "";
    [ObservableProperty] private string status = "Type PUBLISH. Ships meta from the server pin, including heal-hint.json. Refused if that set would be incomplete. Fast-forward only. launcher/ stays.";
    [ObservableProperty] private bool busy;

    [RelayCommand]
    private async Task Publish() => await PublishAsync();

    public async Task<bool> PublishAsync()
    {
        if (Busy)
        {
            return false;
        }

        if (!Confirm.Matches(ConfirmText, "PUBLISH"))
        {
            Status = "Type PUBLISH to confirm the pack push.";
            return false;
        }

        if (MessageBox.Show(
                "Publish the pack to Jefftey97/norcom-pack on main?\n\nFast-forward only. launcher/ stays. Friends-heal meta is stamped from the live server pin, including heal-hint.json (minLauncher 2.0.1).\n\nAdmin is not part of Install-NORCOM.",
                "NORCOM Admin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            Status = "Publish cancelled. Nothing was pushed.";
            return false;
        }

        Busy = true;
        try
        {
            var result = await _publisher.PublishAsync(PackPath, _serverPath(), CancellationToken.None);
            Status = result.Message;
            _redactor.WriteLog(result.Message);
            return result.Pushed;
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnPackPathChanged(string value)
    {
        _settings.PackPath = value.Trim();
        _save();
    }
}
