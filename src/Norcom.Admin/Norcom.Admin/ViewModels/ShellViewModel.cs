using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly AdminSettings _settings;
    private int _autoFixTicks;

    public ShellViewModel()
    {
        var redactor = new SecretRedactor();
        _settings = AdminSettings.Load(AdminPaths.SettingsPath);
        Ops = new OpsViewModel(_settings, redactor, Save);
        Settings = new SettingsViewModel(_settings, () => Ops.ServerPath, () => Ops.RconPort, () => Ops.PasswordForSession(), redactor, Save);
        Mods = new ServerModsViewModel(() => Ops.ServerPath, redactor);
        Configs = new ConfigsViewModel(() => Ops.ServerPath, redactor);
        Publish = new PublishViewModel(_settings, redactor, Save, () => Ops.ServerPath);
        AutoFix = new AutoFixViewModel(_settings, () => Ops.ServerPath, ServerHasProcess, redactor, Save);
        Ops.PathChanged += () =>
        {
            Settings.Load();
            Configs.Refresh();
            Mods.Refresh();
        };
    }

    public void Warmup()
    {
        try
        {
            Ops.Activate();
            Settings.Load();
            Mods.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ops.Status = "Server files are in use. The window stays open.";
        }
    }

    public string VersionText => "1.4.0";

    public string LaneText => "Jeffrey only · never in Install-NORCOM";

    public OpsViewModel Ops { get; }

    public SettingsViewModel Settings { get; }

    public ServerModsViewModel Mods { get; }

    public ConfigsViewModel Configs { get; }

    public PublishViewModel Publish { get; }

    public AutoFixViewModel AutoFix { get; }

    public bool IsDashboard => Page == AdminPage.Dashboard;
    public bool IsPlayers => Page == AdminPage.Players;
    public bool IsConsole => Page == AdminPage.Console;
    public bool IsSettings => Page == AdminPage.Settings;
    public bool IsBackups => Page == AdminPage.Backups;
    public bool IsCrashes => Page == AdminPage.Crashes;
    public bool IsEconomy => Page == AdminPage.Economy;
    public bool IsConfigs => Page == AdminPage.Configs;
    public bool IsPublish => Page == AdminPage.Publish;
    public bool IsAutoFix => Page == AdminPage.AutoFix;

    [ObservableProperty]
    private AdminPage page = AdminPage.Dashboard;

    [ObservableProperty]
    private bool pinChanged;

    [ObservableProperty]
    private string pinNudge = "";

    [RelayCommand]
    private void SelectPage(AdminPage next) => Page = next;

    public void Poll()
    {
        Ops.Poll();
        RefreshPinFlag();
        if (++_autoFixTicks % 30 == 0)
        {
            AutoFix.Tick(announce: false);
        }
    }

    [RelayCommand]
    private async Task PublishFlag()
    {
        var pushed = await Publish.PublishAsync();
        if (!pushed)
        {
            return;
        }

        try
        {
            PinChangedFlag.Delete(Ops.ServerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Publish.Status = Publish.Status + " The pin-changed flag could not be removed.";
        }

        RefreshPinFlag();
    }

    private void RefreshPinFlag()
    {
        if (!PinChangedFlag.TryRead(Ops.ServerPath, out var pin))
        {
            PinChanged = false;
            PinNudge = "";
            return;
        }

        PinChanged = true;
        PinNudge = pin.Length == 0
            ? "Server pin regenerated. Friends meta is stale. Type PUBLISH, then publish the heal set."
            : "Server pin regenerated (" + pin + "). Friends meta is stale. Type PUBLISH, then publish the heal set.";
    }

    partial void OnPageChanged(AdminPage value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsPlayers));
        OnPropertyChanged(nameof(IsConsole));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsBackups));
        OnPropertyChanged(nameof(IsCrashes));
        OnPropertyChanged(nameof(IsEconomy));
        OnPropertyChanged(nameof(IsConfigs));
        OnPropertyChanged(nameof(IsPublish));
        OnPropertyChanged(nameof(IsAutoFix));
        if (value == AdminPage.Configs)
        {
            Configs.Refresh();
        }

        if (value == AdminPage.Economy)
        {
            Mods.Refresh();
        }

        if (value == AdminPage.Settings)
        {
            Settings.Load();
        }
    }

    private void Save() => _settings.Save(AdminPaths.SettingsPath);

    private bool ServerHasProcess()
    {
        var path = Path.Combine(Ops.ServerPath, "norcom-admin.pid");
        if (!File.Exists(path) || !int.TryParse(File.ReadAllText(path).Trim(), out var pid))
        {
            return ServerCommands.JavaAppearsUp(Ops.ServerPath);
        }

        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }
}
