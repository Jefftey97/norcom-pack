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
    private Task Publish() => RunAsync("Packwiz config Publish", meta: false);

    [RelayCommand]
    private Task PublishMeta() => RunAsync("Meta Publish", meta: true);

    public Task<PublishResult> PublishMetaAsync() => RunAsync("Meta Publish", meta: true);

    private async Task<PublishResult> RunAsync(string label, bool meta)
    {
        if (Busy)
        {
            return new PublishResult(false, false, Status);
        }

        if (!Confirm.Matches(ConfirmText, "PUBLISH"))
        {
            Status = "Type PUBLISH to confirm " + label + ".";
            return new PublishResult(false, false, Status);
        }

        if (MessageBox.Show(
                label + " to Jefftey97/norcom-pack on main?\n\nFast-forward only. launcher/ stays. Meta Publish stamps the friends-heal files from the live server pin.\n\nAdmin is not part of Install-NORCOM.",
                "NORCOM Admin",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            Status = label + " cancelled. Nothing was pushed.";
            return new PublishResult(false, false, Status);
        }

        Busy = true;
        try
        {
            var result = meta
                ? await _publisher.PublishMetaAsync(PackPath, _serverPath(), new HttpRemoteMetaCheck(), CancellationToken.None)
                : await _publisher.PublishAsync(PackPath, _serverPath(), CancellationToken.None);
            Status = _redactor.Clean(result.Message);
            _redactor.WriteLog(Status);
            return result with { Message = Status };
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
            return new PublishResult(false, false, Status);
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
