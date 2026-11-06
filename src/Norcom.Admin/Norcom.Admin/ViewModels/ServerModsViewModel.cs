using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class ServerModsViewModel : ObservableObject
{
    private readonly Func<string> _serverPath;
    private readonly SecretRedactor _redactor;

    public ServerModsViewModel(Func<string> serverPath, SecretRedactor redactor)
    {
        _serverPath = serverPath;
        _redactor = redactor;
    }

    public ObservableCollection<ModCard> Mods { get; } = new();

    [ObservableProperty]
    private string status = "Server-side economy, quest, and mod settings. Client graphics stay hidden.";

    [RelayCommand]
    public void Refresh()
    {
        Mods.Clear();
        var directory = _serverPath();
        foreach (var item in ServerModSettings.Scan(directory))
        {
            var card = new ModCard
            {
                Title = item.Title,
                Group = item.Group,
                Note = item.Note,
                Found = item.Found
            };
            foreach (var field in item.Fields)
            {
                card.Fields.Add(new SettingRow
                {
                    File = field.FileRelativeToServer,
                    Key = field.Key,
                    Label = field.Label,
                    Hint = field.Hint,
                    IsBool = field.Kind == "bool",
                    Text = field.Value,
                    Flag = field.Value.Equals("true", StringComparison.OrdinalIgnoreCase)
                });
            }

            Mods.Add(card);
        }

        var found = Mods.Count(card => card.Found);
        Status = Directory.Exists(directory)
            ? found + " server mods have config files. Quest cards appear when a quest config is on the server. Client graphics stay hidden."
            : "Server folder was not found.";
    }

    [RelayCommand]
    private void Save(ModCard? card)
    {
        if (card is not { Found: true })
        {
            return;
        }

        if (!Ask.Yes("Save " + card.Title + " settings?"))
        {
            Status = "Save cancelled.";
            return;
        }

        try
        {
            var edits = card.Fields.Select(field => new ServerSettingEdit(
                field.File,
                field.Key,
                field.IsBool ? (field.Flag ? "true" : "false") : field.Text)).ToList();
            ServerModSettings.Save(_serverPath(), edits);
            Status = "Saved " + card.Title + ". The previous file is in config/.norcom-backup.";
            _redactor.WriteLog("Saved server mod settings for " + card.Title);
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }
}
