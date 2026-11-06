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
    [ObservableProperty] private string status = "Type PUBLISH. Refused unless pack meta matches the server pin. Fast-forward only. launcher/ stays.";
    [ObservableProperty] private bool busy;

    [RelayCommand]
    private async Task Publish()
    {
        if (Busy)
        {
            return;
        }

        if (!Confirm.Matches(ConfirmText, "PUBLISH"))
        {
            Status = "Type PUBLISH to confirm the pack push.";
            return;
        }

        if (MessageBox.Show(
                "Publish the pack to Jefftey97/norcom-pack on main?\n\nThis is a normal push. It will not force-push. launcher/ stays, and installer-version.txt is removed from the packwiz index.\n\nAdmin is not part of Install-NORCOM.",
                "NORCOM Admin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Busy = true;
        try
        {
            var result = await _publisher.PublishAsync(PackPath, _serverPath(), CancellationToken.None);
            Status = result.Message;
            _redactor.WriteLog(result.Message);
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
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
