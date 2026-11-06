using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class OpsViewModel : ObservableObject
{
    private readonly AdminSettings _settings;
    private readonly SecretRedactor _redactor;
    private readonly Action _save;
    private readonly ServerSession _session = new();
    private readonly RconClient _rcon = new();
    private readonly StringBuilder _console = new();
    private readonly ObservableCollection<string> _players = new();
    private readonly ObservableCollection<BackupEntry> _backups = new();
    private readonly ObservableCollection<string> _crashes = new();
    private bool _live;
    private string _password = "";
    private long _logPosition = -1;
    private int _ticks;
    private bool _sparkBusy;
    private TimeSpan _cpuLast = TimeSpan.Zero;
    private DateTime _cpuWall = DateTime.MinValue;
    private readonly TrendBuffer _trend = new();
    private SparkReport _report = SparkReport.Empty;
    private bool _armSparkUrl;
    private string _playerLine = "—";
    private int _playersOnline = -1;

    [ObservableProperty]
    private bool stopEnabled = true;
    private bool _localProbe;
    private int _probeBusy;

    public OpsViewModel(AdminSettings settings, SecretRedactor redactor, Action save)
    {
        _settings = settings;
        _redactor = redactor;
        _save = save;
        ServerPath = settings.ServerPath;
        RconPort = settings.RconPort;
    }

    public void Activate()
    {
        if (_live)
        {
            return;
        }

        _live = true;
        try
        {
            LoadSecretFromServerFile();
            RefreshPresence();
            _ = RefreshLocalProbe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Server files are in use. The window stays open.";
        }
    }

    public ObservableCollection<string> Players => _players;

    public ObservableCollection<BackupEntry> Backups => _backups;

    public ObservableCollection<string> Crashes => _crashes;

    public MetricTile Online { get; } = new("Online", "level lock, fabric java, or local probe");

    public MetricTile PlayerMetric { get; } = new("Players", "RCON list");

    public MetricTile Tps { get; } = new("TPS", "10s / 1m / 5m / 15m");

    public MetricTile Mspt { get; } = new("MSPT", "1m min / med / p95 / max");

    public MetricTile GcYoung { get; } = new("GC young", "count · avg pause · frequency");

    public MetricTile GcOld { get; } = new("GC old", "count · avg pause · frequency");

    public MetricTile Heap { get; } = new("Java heap", "used / max · Xms = Xmx = 8G");

    public MetricTile Ram { get; } = new("System RAM", "host");

    public MetricTile Swap { get; } = new("Swap", "critical at 50% used");

    public MetricTile Cpu { get; } = new("CPU", "process · system, 1m");

    public MetricTile Disk { get; } = new("Disk", "used on the server drive");

    public MetricTile Ping { get; } = new("Ping", "median / p95");

    public MetricTile World { get; } = new("World", "entities · loaded chunks");

    public MetricTile Uptime { get; } = new("Uptime", "this panel's Java");

    public MetricTile Playit { get; } = new("playit", "hostname only");

    public MetricTile LastBackup { get; } = new("Last backup", "zip only while the world is quiet");

    public MetricTile Health { get; } = new("Health", "MSPT, disk, swap, tunnel");

    public IReadOnlyList<MetricTile> Metrics =>
    [
        Online, PlayerMetric, Tps, Mspt, GcYoung, GcOld, Heap, Ram, Swap, Cpu, Disk, Ping, World, Uptime, Playit, LastBackup, Health
    ];

    public event Action? PathChanged;

    [ObservableProperty]
    private string serverPath = "";

    [ObservableProperty]
    private int rconPort = 25575;

    [ObservableProperty]
    private string status = "Point this at the server folder on Jeffrey's PC.";

    [ObservableProperty]
    private string consoleText = "Console output appears here. The RCON password is never written into it.";

    [ObservableProperty]
    private string commandText = "";

    [ObservableProperty]
    private string playerName = "";

    [ObservableProperty]
    private bool serverPresent;

    [ObservableProperty]
    private bool hasPassword;

    [ObservableProperty]
    private bool whitelistOn;

    [ObservableProperty]
    private string accessLine = "Whitelist and ops are read from the server files.";

    [ObservableProperty]
    private string featuresLine = "Economy and quest folders are listed when they exist.";

    [ObservableProperty]
    private string crashInbox = "Crash reports and the latest.log tail appear here.";

    [ObservableProperty]
    private string lastFault = "No crash reports.";

    [ObservableProperty]
    private string frozenNotice = "";

    [ObservableProperty]
    private string selectedBackup = "";

    [ObservableProperty]
    private string trendStrip = "Waiting for Spark.";

    public string PasswordForSession() => _password;

    public void SetPassword(string? password)
    {
        _password = password ?? "";
        _redactor.Remember(_password);
        HasPassword = _password.Length > 0;
    }

    [RelayCommand]
    private void SavePassword()
    {
        if (!ServerPresent)
        {
            Status = "The server folder is not on this PC, so the password was kept in memory only.";
            return;
        }

        if (_password.Length == 0)
        {
            Status = "Type a new RCON password first. The current one is not shown.";
            return;
        }

        if (!Ask.Yes("Write the RCON password into server.properties?"))
        {
            Status = "Password save cancelled. server.properties was not changed.";
            return;
        }

        ServerProperties.WriteSecret(Path.Combine(ServerPath, "server.properties"), _password);
        _redactor.WriteLog("RCON password updated on the server file");
        Status = "RCON password stored in server.properties. It was not written to the admin log.";
    }

    [RelayCommand]
    private void Start() => RunStart(StartReason.You);

    [RelayCommand]
    private async Task Stop()
    {
        if (await PlayersBlockAsync("Stop"))
        {
            return;
        }

        if (!Ask.Yes("Stop the server?"))
        {
            Status = "Stop cancelled.";
            return;
        }

        Status = await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.You);
        _redactor.WriteLog(Status);
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (await PlayersBlockAsync("Soft restart"))
        {
            return;
        }

        if (!Ask.Yes("Soft restart the server? It stops, waits until quiet, then starts. Java is not killed."))
        {
            Status = "Soft restart cancelled.";
            return;
        }

        Status = ServerSession.Tag(StartReason.You, "Soft restart: RCON stop, wait until quiet, then start. Java is not killed.");
        await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.You, allowKill: false);
        var quiet = await _session.WaitUntilQuiet(ServerPath, TimeSpan.FromSeconds(20), CancellationToken.None);
        if (!quiet || ServerCommands.JavaAppearsUp(ServerPath))
        {
            Status = ServerSession.Tag(StartReason.You, "Soft restart did not start again. Java is still up, so it was not killed.");
            return;
        }

        _localProbe = false;
        RunStart(StartReason.You);
    }

    [RelayCommand]
    private async Task CrashRestart()
    {
        if (!Ask.Yes("Close a hung server process and start again?"))
        {
            Status = "Hung-process restart cancelled.";
            return;
        }

        Status = await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.Crash, allowKill: true);
        await Task.Delay(1200);
        if (ServerPresence.IsOnline(ServerPath, localProbeOk: false))
        {
            Status = ServerSession.Tag(StartReason.Crash, "Start was not run again. The world lock or Fabric Java is still up.");
            return;
        }

        _localProbe = false;
        RunStart(StartReason.Crash);
    }

    [RelayCommand]
    private async Task Spark()
    {
        if (!HasPassword)
        {
            Status = "RCON password is required for Spark.";
            return;
        }

        _armSparkUrl = true;
        var profiler = await SendRconAsync("spark profiler start --timeout 60");
        Status = string.IsNullOrWhiteSpace(profiler)
            ? "Spark profiler started for 60 seconds. The profile URL opens when it arrives."
            : profiler;
        WatchSparkUrl(profiler);
        _ = PollSpark();
    }

    [RelayCommand]
    private void Backup()
    {
        if (ServerPresence.IsOnline(ServerPath, _localProbe))
        {
            Ask.Warn(BackupPolicy.LiveWorldMessage);
            Status = BackupPolicy.LiveWorldMessage;
            _redactor.WriteLog(Status);
            return;
        }

        var utc = DateTime.UtcNow;
        if (BackupPolicy.IsDuplicate(
            DenverClock.Now(),
            BackupSignals.FreshAutoZip(ServerPath, utc),
            BackupSignals.MaintenanceFlag(ServerPath, utc)))
        {
            Status = BackupPolicy.SkipMessage;
            _redactor.WriteLog(Status);
            return;
        }

        if (!Ask.Yes("Zip the quiet world now?"))
        {
            Status = "Backup cancelled. Nothing was zipped.";
            return;
        }

        if (ServerPresence.IsOnline(ServerPath, _localProbe))
        {
            Ask.Warn(BackupPolicy.LiveWorldMessage);
            Status = BackupPolicy.LiveWorldMessage;
            _redactor.WriteLog(Status);
            return;
        }

        try
        {
            var zip = BackupArchive.CreateZip(ServerPath);
            Status = "World backup zipped. Java was not writing.";
            _redactor.WriteLog("Backup " + zip);
            RefreshOverview();
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }

    [RelayCommand]
    private void Restore()
    {
        var entry = _backups.FirstOrDefault(item => item.Name == SelectedBackup);
        if (entry == null)
        {
            Status = "Pick a backup first.";
            return;
        }

        if (!Ask.Yes("Restore " + entry.Name + "? The current world folder is moved aside first."))
        {
            Status = "Restore cancelled.";
            return;
        }

        try
        {
            BackupArchive.Restore(ServerPath, entry.FullPath);
            Status = "Restored " + entry.Name + ". The previous world folder was kept beside the server.";
            _redactor.WriteLog(Status);
            RefreshOverview();
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!ServerPresent)
        {
            Status = "Server folder was not found.";
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", ServerPath) { UseShellExecute = true });
            return;
        }

        Status = "Open " + ServerPath + " on Jeffrey's PC.";
    }

    [RelayCommand]
    private async Task SendCommand()
    {
        var command = (CommandText ?? "").Trim();
        if (command.Length == 0)
        {
            return;
        }

        AppendConsole("> " + _redactor.Clean(command));
        CommandText = "";
        Status = await SendRconAsync(command);
        AppendConsole(_redactor.Clean(Status));
    }

    public void Poll()
    {
        if (!ServerPresent)
        {
            return;
        }

        var chunk = LogTail.ReadFrom(Path.Combine(ServerPath, "logs", "latest.log"), ref _logPosition, _redactor);
        if (chunk.Length > 0)
        {
            _console.Append(chunk);
            if (_console.Length > 50000)
            {
                _console.Remove(0, _console.Length - 40000);
            }

            ConsoleText = _console.ToString();
            WatchSparkUrl(chunk);
        }

        if (++_ticks % 5 == 0)
        {
            RefreshOverview();
            _ = RefreshLocalProbe();
        }

        if (_ticks % 15 == 0)
        {
            _ = PollSpark();
        }
    }

    [RelayCommand]
    private async Task WhitelistAdd() => Status = await AccessAsync("whitelist add " + CleanName(PlayerName));

    [RelayCommand]
    private async Task WhitelistRemove() => Status = await PlayerChange("whitelist remove", "Remove this player from the whitelist?");

    [RelayCommand]
    private async Task OpAdd() => Status = await PlayerChange("op", "Give this player operator?");

    [RelayCommand]
    private async Task Deop() => Status = await PlayerChange("deop", "Remove operator from this player?");

    [RelayCommand]
    private async Task Kick() => Status = await PlayerChange("kick", "Kick this player?");

    [RelayCommand]
    private async Task Ban() => Status = await PlayerChange("ban", "Ban this player?");

    [RelayCommand]
    private async Task WhitelistToggle()
    {
        var enable = !WhitelistOn;
        if (!Ask.Yes(enable ? "Turn the whitelist on?" : "Turn the whitelist off?"))
        {
            Status = "Whitelist change cancelled.";
            return;
        }

        try
        {
            var path = Path.Combine(ServerPath, "server.properties");
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            File.WriteAllText(path, ServerProperties.Upsert(existing, new Dictionary<string, string>
            {
                ["white-list"] = enable ? "true" : "false"
            }));
            WhitelistOn = enable;
            if (HasPassword)
            {
                await _rcon.CommandAsync("127.0.0.1", RconPort, _password, enable ? "whitelist on" : "whitelist off", CancellationToken.None);
            }

            Status = enable ? "Whitelist is on." : "Whitelist is off.";
            _redactor.WriteLog(Status);
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }

    private void RunStart(StartReason reason)
    {
        if (ServerPresence.IsOnline(ServerPath, _localProbe))
        {
            Status = "The server is already online. Start was not run again.";
            return;
        }

        try
        {
            Status = _session.Start(ServerPath, AdminSettings.LiveHeapMb, reason);
            _redactor.WriteLog(Status);
        }
        catch (Exception ex)
        {
            Status = _redactor.Clean(ex.Message);
        }
    }

    private async Task PollSpark()
    {
        if (_sparkBusy || !HasPassword)
        {
            return;
        }

        _sparkBusy = true;
        try
        {
            var parts = new List<string>();
            foreach (var command in new[] { "spark tps", "spark health show", "spark gc", "spark ping" })
            {
                try
                {
                    parts.Add(await _rcon.CommandAsync("127.0.0.1", RconPort, _password, command, CancellationToken.None));
                }
                catch (Exception ex)
                {
                    parts.Add(ex.Message);
                }
            }

            _report = SparkReportParser.Parse(_redactor.Clean(string.Join("\n", parts)));
            RecordTrend(_report);
            RefreshOverview();
        }
        finally
        {
            _sparkBusy = false;
        }
    }

    private void WatchSparkUrl(string? text)
    {
        if (!_armSparkUrl || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var match = Regex.Match(text, @"https://spark\.lucko\.me/\S+");
        if (!match.Success)
        {
            return;
        }

        _armSparkUrl = false;
        Status = "Spark profile " + match.Value;
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(match.Value) { UseShellExecute = true });
        }
    }

    private async Task<string> SendRconAsync(string command)
    {
        if (!HasPassword)
        {
            return "RCON password is required.";
        }

        try
        {
            return _redactor.Clean(await _rcon.CommandAsync("127.0.0.1", RconPort, _password, command, CancellationToken.None));
        }
        catch (Exception ex)
        {
            return _redactor.Clean(ex.Message);
        }
    }

    private async Task<string> PlayerChange(string verb, string question)
    {
        var name = CleanName(PlayerName);
        if (name.Length == 0)
        {
            return "Enter a player name made of letters, numbers, or underscores.";
        }

        if (!Ask.Yes(question + "\n\n" + name))
        {
            return verb + " cancelled.";
        }

        return await AccessAsync(verb + " " + name);
    }

    private async Task<string> AccessAsync(string command)
    {
        if (!HasPassword)
        {
            return "RCON password is required before changing players.";
        }

        if (command.EndsWith(' '))
        {
            return "Enter a player name made of letters, numbers, or underscores.";
        }

        try
        {
            var text = await _rcon.CommandAsync("127.0.0.1", RconPort, _password, command, CancellationToken.None);
            RefreshOverview();
            return _redactor.Clean(text);
        }
        catch (Exception ex)
        {
            return _redactor.Clean(ex.Message);
        }
    }

    private void AppendConsole(string line)
    {
        _console.AppendLine(line);
        ConsoleText = _console.ToString();
    }

    private static string CleanName(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length is < 1 or > 16 || text.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            return "";
        }

        return text;
    }

    private void RefreshOverview()
    {
        try
        {
            RefreshOverviewCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Server files are in use. The window stays open.";
        }
    }

    private void RefreshOverviewCore()
    {
        var overview = ServerOverviewReader.Read(ServerPath);
        var drive = HostSnapshot.ReadDrive(ServerPath);
        var ram = HostSnapshot.ReadRam();
        var swap = HostSnapshot.ReadSwap();
        var backup = BackupArchive.Age(ServerPath);
        var online = ServerPresence.IsOnline(ServerPath, _localProbe);
        Online.Value = !overview.FolderExists ? "Missing" : online ? "Online" : "Offline";
        Online.Alert = overview.FolderExists && !online;
        Online.Hint = ServerPresence.LevelName(ServerPath) + " session.lock, fabric java, or local SLP/RCON";
        PlayerMetric.Value = _playerLine;
        Tps.Value = _report.TpsLine;
        Mspt.Value = _report.MsptLine;
        Mspt.Alert = _report.MsptAbove40;
        GcYoung.Value = SparkReport.GcLine(_report.Young);
        GcYoung.Hint = _report.Young?.Name ?? "count · avg pause · frequency";
        GcOld.Value = SparkReport.GcLine(_report.Old);
        GcOld.Hint = _report.Old?.Name ?? "count · avg pause · frequency";
        Heap.Value = _report.HeapLine;
        Ram.Value = ram.Text;
        Swap.Value = swap.Text;
        Swap.Alert = HostHealth.SwapCritical(swap.Used, swap.Total);
        Cpu.Value = _report.CpuProcess1m == null && _report.CpuSystem1m == null ? SampleCpu() : _report.CpuLine;
        Disk.Value = drive.Text;
        Disk.Hint = drive.Total > 0 ? drive.Label + " · flag at 80% used" : "used on the server drive";
        Disk.Alert = HostHealth.DiskHigh(drive.Percent);
        Ping.Value = _report.PingLine;
        World.Value = _report.WorldLine;
        Uptime.Value = UptimeText();
        Playit.Value = overview.Tunnel;
        Playit.Alert = PlayitAddress.IsDown(overview.Tunnel);
        LastBackup.Value = backup;
        var health = ServerHealth.Score(Mspt.Alert, Disk.Alert, Swap.Alert, Playit.Alert);
        Health.Value = health;
        Health.Alert = health != "Clear";
        WhitelistOn = overview.WhiteList.Equals("true", StringComparison.OrdinalIgnoreCase);
        AccessLine = "Whitelist: " + Join(overview.Whitelist) + ". Ops: " + Join(overview.Ops) + ".";
        FeaturesLine = overview.Features;
        var crashes = CrashReports.Read(ServerPath);
        LastFault = crashes.LastFault;
        CrashInbox = crashes.LastFault + "\n" + Inbox(ServerPath);
        FrozenNotice = FrozenText();
        Replace(_backups, BackupArchive.List(ServerPath));
        Replace(_crashes, crashes.Files.ToList());
    }

    private void RecordTrend(SparkReport report)
    {
        if (double.TryParse(report.Tps1m, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tps))
        {
            _trend.Add(tps);
        }
        else if (double.TryParse(report.MsptMedian, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mspt))
        {
            _trend.Add(mspt);
        }
        else
        {
            return;
        }

        TrendStrip = _trend.Strip();
    }

    private string SampleCpu()
    {
        if (_session.TrackedPid is not int pid)
        {
            return "—";
        }

        try
        {
            var process = Process.GetProcessById(pid);
            var now = DateTime.UtcNow;
            var cpu = process.TotalProcessorTime;
            if (_cpuWall == DateTime.MinValue)
            {
                _cpuWall = now;
                _cpuLast = cpu;
                return "—";
            }

            var percent = CpuSample.Percent(_cpuLast, _cpuWall, cpu, now, Environment.ProcessorCount);
            _cpuLast = cpu;
            _cpuWall = now;
            return "proc " + SparkReportParser.FormatPercent(percent) + " · sys —";
        }
        catch
        {
            return "—";
        }
    }

    private string UptimeText()
    {
        var started = _session.StartedAt;
        if (started == null)
        {
            return "Not attached";
        }

        var span = DateTime.Now - started.Value;
        return (int)span.TotalHours + "h " + span.Minutes.ToString("00") + "m";
    }

    private string FrozenText()
    {
        var log = Path.Combine(ServerPath, "logs", "latest.log");
        DateTime? last = File.Exists(log) ? File.GetLastWriteTimeUtc(log) : null;
        return AutoFix.ShouldRestartFrozen(
            _settings.AutoFix.FrozenServerRestart,
            ServerCommands.JavaAppearsUp(ServerPath),
            last,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(_settings.AutoFix.FrozenAfterMinutes))
            ? "Log looks frozen. Soft restart stays a typed RESTART."
            : "";
    }

    private static string Inbox(string path)
    {
        var crashes = Path.Combine(path, "crash-reports");
        var names = Directory.Exists(crashes)
            ? Directory.GetFiles(crashes).OrderByDescending(File.GetLastWriteTimeUtc).Take(3).Select(Path.GetFileName)
            : Array.Empty<string?>();
        var log = Path.Combine(path, "logs", "latest.log");
        var tail = "No latest.log yet.";
        try
        {
            if (File.Exists(log))
            {
                tail = string.Join("\n", SharedFile.ReadLines(log).TakeLast(8));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tail = "latest.log is in use.";
        }
        var head = names.Any() ? string.Join(", ", names) : "No crash reports.";
        return head + "\n" + tail;
    }

    private static IReadOnlyList<string> CrashNames(string path)
    {
        var crashes = Path.Combine(path, "crash-reports");
        if (!Directory.Exists(crashes))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(crashes).OrderByDescending(File.GetLastWriteTimeUtc).Take(20).Select(Path.GetFileName).Where(name => name != null).Cast<string>().ToList();
    }

    private static string Join(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> next)
    {
        if (target.Count == next.Count && target.Zip(next).All(pair => EqualityComparer<T>.Default.Equals(pair.First, pair.Second)))
        {
            return;
        }

        target.Clear();
        foreach (var item in next)
        {
            target.Add(item);
        }
    }

    public void RefreshPresence()
    {
        ServerPresent = Directory.Exists(ServerPath);
        RefreshOverview();
        if (!ServerPresent)
        {
            Status = "Server folder was not found. The default is G:\\MinecraftServer on Jeffrey's PC.";
            return;
        }

        Status = _session.Inspect(ServerPath, AdminSettings.LiveHeapMb).Message;
    }

    private async Task<bool> PlayersBlockAsync(string action)
    {
        var online = await PlayersOnlineNowAsync();
        if (online <= 0)
        {
            return false;
        }

        _ = action;
        const string message = "Players online — Stop/Soft locked";
        Ask.Warn(message);
        Status = message;
        return true;
    }

    private async Task<int> PlayersOnlineNowAsync()
    {
        var counts = new List<int>();
        var slp = await ServerListPing.PlayersAsync("127.0.0.1", 25565, TimeSpan.FromMilliseconds(800));
        if (slp is int seen)
        {
            counts.Add(seen);
        }

        if (_password.Length > 0)
        {
            try
            {
                var text = await _rcon.CommandAsync("127.0.0.1", RconPort, _password, "list", CancellationToken.None);
                if (PlayerList.TryParse(_redactor.Clean(text), out var online, out var max, out var names))
                {
                    RememberPlayers(online, max, names);
                    counts.Add(online);
                }
            }
            catch
            {
            }
        }

        if (counts.Count == 0)
        {
            return _playersOnline;
        }

        var count = counts.Max();
        ApplyBuddy(count);
        return count;
    }

    private void ApplyBuddy(int online)
    {
        _playersOnline = online;
        StopEnabled = online <= 0;
    }

    private void RememberPlayers(int online, int max, IReadOnlyList<string> names)
    {
        _playersOnline = online;
        _playerLine = online + " / " + max;
        PlayerMetric.Value = _playerLine;
        Replace(_players, names.ToList());
    }

    private async Task RefreshLocalProbe()
    {
        if (Interlocked.Exchange(ref _probeBusy, 1) == 1)
        {
            return;
        }

        try
        {
            var slp = await ServerListPing.TryAsync("127.0.0.1", 25565, TimeSpan.FromMilliseconds(800));
            var slpPlayers = await ServerListPing.PlayersAsync("127.0.0.1", 25565, TimeSpan.FromMilliseconds(800));
            var rcon = false;
            int? listed = null;
            if (_password.Length > 0)
            {
                try
                {
                    var text = await _rcon.CommandAsync("127.0.0.1", RconPort, _password, "list", CancellationToken.None);
                    if (PlayerList.TryParse(_redactor.Clean(text), out var online, out var max, out var names))
                    {
                        rcon = true;
                        listed = online;
                        RememberPlayers(online, max, names);
                    }
                    else if (!string.IsNullOrWhiteSpace(text))
                    {
                        rcon = true;
                    }
                }
                catch
                {
                }
            }

            var counts = new List<int>();
            if (listed is int fromList)
            {
                counts.Add(fromList);
            }

            if (slpPlayers is int fromSlp)
            {
                counts.Add(fromSlp);
            }

            if (counts.Count > 0)
            {
                ApplyBuddy(counts.Max());
            }

            _localProbe = slp || rcon;
            if (_localProbe && Directory.Exists(ServerPath))
            {
                Online.Value = "Online";
                Online.Alert = false;
            }
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _probeBusy, 0);
        }
    }

    private void LoadSecretFromServerFile()
    {
        var path = Path.Combine(ServerPath, "server.properties");
        if (!File.Exists(path))
        {
            return;
        }

        if (ServerProperties.Read(File.ReadAllText(path)).TryGetValue("rcon.password", out var value) && value.Length > 0)
        {
            _password = value;
            _redactor.Remember(_password);
            HasPassword = true;
        }
    }

    partial void OnServerPathChanged(string value)
    {
        _settings.ServerPath = value.Trim();
        if (!_live)
        {
            return;
        }

        _save();
        _logPosition = -1;
        RefreshPresence();
        LoadSecretFromServerFile();
        PathChanged?.Invoke();
    }

    partial void OnRconPortChanged(int value)
    {
        _settings.RconPort = Math.Clamp(value, 1, 65535);
        if (!_live)
        {
            return;
        }

        if (_settings.RconPort != value)
        {
            RconPort = _settings.RconPort;
            return;
        }

        _save();
    }
}
