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
        Bank = new BankViewModel(() => Ops.ServerPath, () => Ops.RconPort, () => Ops.PasswordForSession(), redactor, new RconClient());
        Configs = new ConfigsViewModel(() => Ops.ServerPath, redactor);
        Publish = new PublishViewModel(_settings, redactor, Save, () => Ops.ServerPath);
        AutoFix = new AutoFixViewModel(_settings, () => Ops.ServerPath, ServerHasProcess, redactor, Save);
        Ops.PathChanged += () =>
        {
            Settings.Load();
            Configs.Refresh();
            Mods.Refresh();
            Bank.Refresh();
        };
    }

    public void Warmup()
    {
        try
        {
            Ops.Activate();
            Settings.Load();
            Mods.Refresh();
            Bank.Refresh();
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

    public BankViewModel Bank { get; }

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

    [ObservableProperty]
    private string pinStrip = "Publish needed";

    [ObservableProperty]
    private bool healBlocked;

    private DateTime _lastRawCheck = DateTime.MinValue;

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
        var result = await Publish.PublishMetaAsync();
        if (!result.ClearFlag)
        {
            return;
        }

        try
        {
            PinChangedFlag.Delete(Ops.ServerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Publish.Status = _redactorSafe(Publish.Status + " The pin-changed flag could not be removed.");
        }

        RefreshPinFlag();
    }

    private void RefreshPinFlag()
    {
        var sot = NorcomPin.TryRead(Ops.ServerPath);
        HealFails.ConsumeClientMarker(Ops.ServerPath, sot?.pin ?? "");
        PinStrip = PackPinMatches(sot?.pin, PackPin(Publish.PackPath)) ? "pin OK" : "Publish needed";
        if (!PinChangedFlag.TryRead(Ops.ServerPath, out var flagged))
        {
            PinChanged = false;
            HealBlocked = false;
            return;
        }

        var pin = flagged.Length > 0 ? flagged : sot?.pin ?? "";
        var index = IndexSha(Publish.PackPath);
        HealBlocked = HealFails.BlocksMetaPublish(Ops.ServerPath, pin);
        PinChanged = true;
        PinNudge = HealBlocked
            ? "pin " + NorcomPin.Prefix(pin) + " index " + NorcomPin.Prefix(index) + " same-as-raw: no. Launcher PinGate or a dual-sided heal may be the issue. Meta Publish is not offered."
            : "pin " + NorcomPin.Prefix(pin) + " index " + NorcomPin.Prefix(index) + " same-as-raw: checking. Click Meta Publish. No typed confirm.";
        if (!HealBlocked && (DateTime.UtcNow - _lastRawCheck).TotalSeconds > 20)
        {
            _lastRawCheck = DateTime.UtcNow;
            _ = MarkAlreadyLiveAsync(pin);
        }
    }

    private async Task MarkAlreadyLiveAsync(string pin)
    {
        if (!NorcomPin.TryReadProof(Ops.ServerPath, out var proof, out _))
        {
            PinNudge = "pin " + NorcomPin.Prefix(pin) + " marble — same-as-raw: no. Live admin pin could not be read. Not ALREADY_LIVE.";
            return;
        }

        if (pin.Trim().Length > 0 && !string.Equals(pin.Trim(), proof.Pin, StringComparison.OrdinalIgnoreCase))
        {
            PinNudge = "pin " + NorcomPin.Prefix(pin) + " marble " + NorcomPin.Prefix(proof.MarbleSha) + " same-as-raw: no. Live pin is " + NorcomPin.Prefix(proof.Pin) + ". Not ALREADY_LIVE.";
            return;
        }

        MetaAudit audit;
        try
        {
            audit = await MetaRemote.AuditContentsAsync(proof, new HttpRemoteMetaCheck(), CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            PinNudge = "pin " + NorcomPin.Prefix(proof.Pin) + " marble " + NorcomPin.Prefix(proof.MarbleSha) + " same-as-raw: no. Contents API check failed. Not ALREADY_LIVE.";
            return;
        }

        if (!audit.Ok)
        {
            PinNudge = "pin " + NorcomPin.Prefix(proof.Pin) + " marble " + NorcomPin.Prefix(proof.MarbleSha) + " same-as-raw: no. " + audit.Report + " Not ALREADY_LIVE.";
            return;
        }

        try
        {
            PinChangedFlag.Delete(Ops.ServerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        PinChanged = false;
        PinNudge = "ALREADY_LIVE pin " + NorcomPin.Prefix(proof.Pin) + " marble " + NorcomPin.Prefix(proof.MarbleSha) + " contents API matches LIVE. Flag cleared. Nothing was pushed.";
    }

    private static bool PackPinMatches(string? serverPin, string packPin) =>
        !string.IsNullOrWhiteSpace(serverPin) &&
        string.Equals(serverPin.Trim(), packPin.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string PackPin(string packDirectory)
    {
        try
        {
            var path = Path.Combine(packDirectory, "meta", "server-pin.json");
            if (!File.Exists(path))
            {
                return "";
            }

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("pin", out var value) ? value.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string IndexSha(string packDirectory)
    {
        try
        {
            var path = Path.Combine(packDirectory, "meta", "server-compat.json");
            if (!File.Exists(path))
            {
                return "";
            }

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("indexSha256", out var value) ? value.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string _redactorSafe(string text) => new SecretRedactor().Clean(text);

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
            Bank.Refresh();
        }

        if (value == AdminPage.Settings)
        {
            Settings.Load();
        }
    }

    private void Save() => _settings.Save(AdminPaths.SettingsPath);

    private bool ServerHasProcess() =>
        ServerPresence.IsOnline(Ops.ServerPath, localProbeOk: false);
}
