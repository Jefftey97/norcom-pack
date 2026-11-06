using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class ConfigsViewModel : ObservableObject
{
    private readonly Func<string> _serverPath;
    private readonly SecretRedactor _redactor;

    public ConfigsViewModel(Func<string> serverPath, SecretRedactor redactor)
    {
        _serverPath = serverPath;
        _redactor = redactor;
    }

    public ObservableCollection<string> Files { get; } = new();

    [ObservableProperty] private string? selectedFile;
    [ObservableProperty] private string editor = "";
    [ObservableProperty] private string status = "Config files under the server. Secrets and client graphics are hidden.";

    [RelayCommand]
    public void Refresh()
    {
        Files.Clear();
        var directory = _serverPath();
        if (!Directory.Exists(directory))
        {
            Status = "Server folder was not found.";
            return;
        }

        foreach (var item in ServerCommands.ListConfigs(directory))
        {
            if (!ServerModSettings.IsHiddenFromAdmin(item))
            {
                Files.Add(item);
            }
        }

        Status = Files.Count + " config files. Client graphics and secret files are hidden.";
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(SelectedFile))
        {
            return;
        }

        try
        {
            ServerCommands.SaveConfig(_serverPath(), SelectedFile, Editor);
            Status = "Saved " + SelectedFile + ". The previous copy is in config/.norcom-backup.";
            _redactor.WriteLog("Saved config " + SelectedFile);
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }

    partial void OnSelectedFileChanged(string? value) => LoadSelected();

    private void LoadSelected()
    {
        if (string.IsNullOrWhiteSpace(SelectedFile))
        {
            Editor = "";
            return;
        }

        try
        {
            var text = ServerCommands.ReadConfig(_serverPath(), SelectedFile);
            if (text.Length > 200000)
            {
                text = text[..200000] + "\n… truncated …";
            }

            Editor = _redactor.Clean(text);
            Status = SelectedFile;
        }
        catch (Exception ex)
        {
            Editor = "";
            Status = _redactor.Clean(ex.Message);
        }
    }
}
