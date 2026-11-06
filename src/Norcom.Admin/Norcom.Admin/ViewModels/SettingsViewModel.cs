using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] Structured =
    [
        "motd", "difficulty", "gamemode", "max-players", "view-distance", "spawn-protection",
        "level-name", "pvp", "white-list", "enforce-whitelist", "online-mode", "allow-flight",
        "enable-rcon", "rcon.port", "server-port"
    ];

    private readonly AdminSettings _settings;
    private readonly Func<string> _serverPath;
    private readonly Func<int> _rconPort;
    private readonly Func<string> _password;
    private readonly SecretRedactor _redactor;
    private readonly Action _save;
    private readonly RconClient _rcon = new();
    public SettingsViewModel(AdminSettings settings, Func<string> serverPath, Func<int> rconPort, Func<string> password, SecretRedactor redactor, Action save)
    {
        _settings = settings;
        _serverPath = serverPath;
        _rconPort = rconPort;
        _password = password;
        _redactor = redactor;
        _save = save;
        DayTicks = settings.DayTicks;
        NightTicks = settings.NightTicks;
    }

    public ObservableCollection<PropertyRow> Extra { get; } = new();

    [ObservableProperty] private string status = "Type TIME to save. simulation-distance is left unchanged.";
    [ObservableProperty] private string confirmText = "";
    [ObservableProperty] private string motd = "NORCOM";
    [ObservableProperty] private string difficulty = "easy";
    [ObservableProperty] private string gamemode = "survival";
    [ObservableProperty] private string maxPlayers = "5";
    [ObservableProperty] private string viewDistance = "16";
    [ObservableProperty] private string simulationDistance = "unchanged";
    [ObservableProperty] private string spawnProtection = "16";
    [ObservableProperty] private string levelName = "world";
    [ObservableProperty] private bool pvp = true;
    [ObservableProperty] private bool whiteList;
    [ObservableProperty] private bool enforceWhitelist;
    [ObservableProperty] private bool onlineMode = true;
    [ObservableProperty] private bool allowFlight;
    [ObservableProperty] private bool keepInventory;
    [ObservableProperty] private bool mobGriefing = true;
    [ObservableProperty] private bool doFireTick = true;
    [ObservableProperty] private bool doWeatherCycle = true;
    [ObservableProperty] private bool doDaylightCycle = true;
    [ObservableProperty] private string spawnRadius = "10";
    [ObservableProperty] private int dayTicks = 12000;
    [ObservableProperty] private int nightTicks = 12000;

    [RelayCommand]
    private void Reload() => Load();

    [RelayCommand]
    private async Task Save()
    {
        if (!Confirm.Matches(ConfirmText, "TIME"))
        {
            Status = "Type TIME before saving settings, game rules, or day and night length.";
            return;
        }

        var directory = _serverPath();
        if (!Directory.Exists(directory))
        {
            Status = "Server folder was not found, so the rules were kept in this window only.";
            return;
        }

        var path = Path.Combine(directory, "server.properties");
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["motd"] = Motd,
            ["difficulty"] = Difficulty,
            ["gamemode"] = Gamemode,
            ["max-players"] = MaxPlayers,
            ["view-distance"] = ViewDistance,
            ["spawn-protection"] = SpawnProtection,
            ["level-name"] = LevelName,
            ["pvp"] = Pvp ? "true" : "false",
            ["white-list"] = WhiteList ? "true" : "false",
            ["enforce-whitelist"] = EnforceWhitelist ? "true" : "false",
            ["online-mode"] = OnlineMode ? "true" : "false",
            ["allow-flight"] = AllowFlight ? "true" : "false",
            ["enable-rcon"] = "true",
            ["rcon.port"] = _rconPort().ToString()
        };
        foreach (var row in Extra)
        {
            if (!ServerProperties.IsLocked(row.Key))
            {
                updates[row.Key] = row.Value;
            }
        }

        File.WriteAllText(path, ServerProperties.Upsert(existing, updates));
        TimeCycle.Save(directory, DayTicks, NightTicks);
        GameRulesFile.Save(directory, new Dictionary<string, string>
        {
            ["keepInventory"] = KeepInventory ? "true" : "false",
            ["mobGriefing"] = MobGriefing ? "true" : "false",
            ["doFireTick"] = DoFireTick ? "true" : "false",
            ["doWeatherCycle"] = DoWeatherCycle ? "true" : "false",
            ["doDaylightCycle"] = DoDaylightCycle ? "true" : "false",
            ["spawnRadius"] = SpawnRadius
        });
        Status = "Saved server settings and game rules. simulation-distance and the RCON password were left as they are.";
        _redactor.WriteLog(Status);
        await PushRulesAsync();
    }

    public void Load()
    {
        var directory = _serverPath();
        var propertiesPath = Path.Combine(directory, "server.properties");
        Extra.Clear();
        if (File.Exists(propertiesPath))
        {
            var map = ServerProperties.PublicView(ServerProperties.Read(File.ReadAllText(propertiesPath)));
            Motd = Get(map, "motd", Motd);
            Difficulty = Get(map, "difficulty", Difficulty);
            Gamemode = Get(map, "gamemode", Gamemode);
            MaxPlayers = Get(map, "max-players", MaxPlayers);
            ViewDistance = Get(map, "view-distance", ViewDistance);
            SimulationDistance = Get(map, "simulation-distance", SimulationDistance);
            SpawnProtection = Get(map, "spawn-protection", SpawnProtection);
            LevelName = Get(map, "level-name", LevelName);
            Pvp = Bool(map, "pvp", Pvp);
            WhiteList = Bool(map, "white-list", WhiteList);
            EnforceWhitelist = Bool(map, "enforce-whitelist", EnforceWhitelist);
            OnlineMode = Bool(map, "online-mode", OnlineMode);
            AllowFlight = Bool(map, "allow-flight", AllowFlight);
            foreach (var pair in map.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (ServerProperties.IsLocked(pair.Key) || Structured.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                Extra.Add(new PropertyRow(pair.Key, pair.Value));
            }
        }

        var timePath = Path.Combine(directory, "config", "norcom-time.properties");
        if (File.Exists(timePath))
        {
            (DayTicks, NightTicks) = TimeCycle.Parse(File.ReadAllText(timePath));
        }

        var rulesPath = Path.Combine(directory, "config", "norcom-gamerules.properties");
        if (File.Exists(rulesPath))
        {
            var rules = ServerProperties.Read(File.ReadAllText(rulesPath));
            KeepInventory = Bool(rules, "keepInventory", KeepInventory);
            MobGriefing = Bool(rules, "mobGriefing", MobGriefing);
            DoFireTick = Bool(rules, "doFireTick", DoFireTick);
            DoWeatherCycle = Bool(rules, "doWeatherCycle", DoWeatherCycle);
            DoDaylightCycle = Bool(rules, "doDaylightCycle", DoDaylightCycle);
            SpawnRadius = Get(rules, "spawnRadius", SpawnRadius);
        }
    }

    private async Task PushRulesAsync()
    {
        var password = _password();
        if (password.Length == 0)
        {
            return;
        }

        try
        {
            (string Key, string Value)[] rules =
            [
                ("keepInventory", KeepInventory ? "true" : "false"),
                ("mobGriefing", MobGriefing ? "true" : "false"),
                ("doFireTick", DoFireTick ? "true" : "false"),
                ("doWeatherCycle", DoWeatherCycle ? "true" : "false"),
                ("doDaylightCycle", DoDaylightCycle ? "true" : "false"),
                ("spawnRadius", SpawnRadius)
            ];
            foreach (var rule in rules)
            {
                await _rcon.CommandAsync("127.0.0.1", _rconPort(), password, "gamerule " + rule.Key + " " + rule.Value, CancellationToken.None);
            }

            Status = "Saved, and the game rules were sent over RCON. simulation-distance was not changed.";
        }
        catch (Exception ex)
        {
            Status = "Saved on disk. RCON did not accept the rules. " + _redactor.Clean(ex.Message);
        }
    }

    partial void OnDayTicksChanged(int value)
    {
        _settings.DayTicks = Math.Clamp(value, 1000, 72000);
        _save();
    }

    partial void OnNightTicksChanged(int value)
    {
        _settings.NightTicks = Math.Clamp(value, 1000, 72000);
        _save();
    }

    private static string Get(IReadOnlyDictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    private static bool Bool(IReadOnlyDictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out var value) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
}
