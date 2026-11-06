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
    private SparkReport _report = SparkReport.Empty;
    private bool _armSparkUrl;
    private string _playerLine = "—";

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
        LoadSecretFromServerFile();
        RefreshPresence();
    }

    public ObservableCollection<string> Players => _players;

    public ObservableCollection<BackupEntry> Backups => _backups;

    public ObservableCollection<string> Crashes => _crashes;

    public MetricTile Online { get; } = new("Online", "process or session.lock");

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
    private string confirmText = "";

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
    private string frozenNotice = "";

    [ObservableProperty]
    private string selectedBackup = "";

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

        ServerProperties.WriteSecret(Path.Combine(ServerPath, "server.properties"), _password);
        _redactor.WriteLog("RCON password updated on the server file");
        Status = "RCON password stored in server.properties. It was not written to the admin log.";
    }

    [RelayCommand]
    private void Start() => RunStart(StartReason.You);

    [RelayCommand]
    private async Task Stop()
    {
        if (!Confirm.Matches(ConfirmText, "STOP"))
        {
            Status = "Type STOP to confirm.";
            return;
        }

        Status = await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.You);
        _redactor.WriteLog(Status);
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (!Confirm.Matches(ConfirmText, "RESTART"))
        {
            Status = "Type RESTART to confirm a soft restart.";
            return;
        }

        Status = ServerSession.Tag(StartReason.You, "Soft restart: RCON stop, then start. The process is not killed.");
        await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.You);
        var quiet = await _session.WaitUntilQuiet(ServerPath, TimeSpan.FromSeconds(20), CancellationToken.None);
        if (!quiet && ServerCommands.JavaAppearsUp(ServerPath))
        {
            Status = ServerSession.Tag(StartReason.You, "Soft restart is waiting. Java is still up, so it was not killed.");
            return;
        }

        RunStart(StartReason.You);
    }

    [RelayCommand]
    private async Task CrashRestart()
    {
        if (!Confirm.Matches(ConfirmText, "CRASH"))
        {
            Status = "Type CRASH to confirm the hung-process path.";
            return;
        }

        Status = await _session.StopAsync("127.0.0.1", RconPort, _password, _rcon, CancellationToken.None, StartReason.Crash, allowKill: true);
        await Task.Delay(1200);
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
    private async Task Backup()
    {
        var utc = DateTime.UtcNow;
        var duplicate = BackupPolicy.IsDuplicate(
            DenverClock.Now(),
            BackupSignals.FreshAutoZip(ServerPath, utc),
            BackupSignals.MaintenanceFlag(ServerPath, utc));
        var writing = ServerCommands.JavaAppearsUp(ServerPath);
        var step = BackupPolicy.Decide(writing, HasPassword, duplicate);
        if (step == BackupStep.Skip)
        {
            Status = BackupPolicy.SkipMessage;
            _redactor.WriteLog(Status);
            return;
        }

        if (step == BackupStep.FlushThenRecheck)
        {
            try
            {
                await _rcon.CommandAsync("127.0.0.1", RconPort, _password, "save-all flush", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Status = _redactor.Clean(ex.Message) + " No zip was made.";
                return;
            }

            utc = DateTime.UtcNow;
            duplicate = BackupPolicy.IsDuplicate(
                DenverClock.Now(),
                BackupSignals.FreshAutoZip(ServerPath, utc),
                BackupSignals.MaintenanceFlag(ServerPath, utc));
            writing = ServerCommands.JavaAppearsUp(ServerPath);
            step = BackupPolicy.AfterFlush(writing, duplicate);
            if (step == BackupStep.Skip)
            {
                Status = BackupPolicy.SkipMessage;
                _redactor.WriteLog(Status);
                return;
            }

            if (step == BackupStep.Refuse)
            {
                Status = BackupPolicy.FlushStillLiveMessage(ServerCommands.WritingReason(ServerPath));
                _redactor.WriteLog(Status);
                return;
            }
        }
        else if (step == BackupStep.Refuse)
        {
            Status = HasPassword
                ? BackupPolicy.RefuseMessage(ServerCommands.WritingReason(ServerPath))
                : BackupPolicy.NoFlushPathMessage;
            _redactor.WriteLog(Status);
            return;
        }

        if (!Confirm.Matches(ConfirmText, "BACKUP"))
        {
            Status = "Type BACKUP to zip a quiet world. A live world is refused. 04:00–04:01 Denver, a fresh auto zip, or maintenance.flag skips a second zip.";
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
        if (!Confirm.Matches(ConfirmText, "RESTORE"))
        {
            Status = "Type RESTORE to confirm. The current world is moved aside first.";
            return;
        }

        var entry = _backups.FirstOrDefault(item => item.Name == SelectedBackup);
        if (entry == null)
        {
            Status = "Pick a backup first.";
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
            _ = RefreshPlayers();
        }

        if (_ticks % 15 == 0)
        {
            _ = PollSpark();
        }
    }

    [RelayCommand]
    private async Task WhitelistAdd() => Status = await AccessAsync("whitelist add " + CleanName(PlayerName));

    [RelayCommand]
    private async Task WhitelistRemove() => Status = await AccessAsync("whitelist remove " + CleanName(PlayerName));

    [RelayCommand]
    private async Task OpAdd()
    {
        if (!Confirm.Matches(ConfirmText, "OP"))
        {
            Status = "Type OP to confirm.";
            return;
        }

        Status = await AccessAsync("op " + CleanName(PlayerName));
    }

    [RelayCommand]
    private async Task Deop()
    {
        if (!Confirm.Matches(ConfirmText, "DEOP"))
        {
            Status = "Type DEOP to confirm.";
            return;
        }

        Status = await AccessAsync("deop " + CleanName(PlayerName));
    }

    [RelayCommand]
    private async Task Kick()
    {
        if (!Confirm.Matches(ConfirmText, "KICK"))
        {
            Status = "Type KICK to confirm.";
            return;
        }

        Status = await AccessAsync("kick " + CleanName(PlayerName));
    }

    [RelayCommand]
    private async Task Ban()
    {
        if (!Confirm.Matches(ConfirmText, "BAN"))
        {
            Status = "Type BAN to confirm.";
            return;
        }

        Status = await AccessAsync("ban " + CleanName(PlayerName));
    }

    [RelayCommand]
    private async Task WhitelistToggle()
    {
        var enable = !WhitelistOn;
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
        var overview = ServerOverviewReader.Read(ServerPath);
        var drive = HostSnapshot.ReadDrive(ServerPath);
        var ram = HostSnapshot.ReadRam();
        var swap = HostSnapshot.ReadSwap();
        var backup = BackupArchive.Age(ServerPath);
        var online = ServerCommands.JavaAppearsUp(ServerPath);
        Online.Value = !overview.FolderExists ? "Missing" : online ? "Online" : "Offline";
        Online.Alert = !online;
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
        CrashInbox = Inbox(ServerPath);
        FrozenNotice = FrozenText();
        Replace(_backups, BackupArchive.List(ServerPath));
        Replace(_crashes, CrashNames(ServerPath));
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
        var tail = File.Exists(log) ? string.Join("\n", File.ReadLines(log).TakeLast(8)) : "No latest.log yet.";
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

    private async Task RefreshPlayers()
    {
        if (_password.Length == 0)
        {
            return;
        }

        try
        {
            var text = await _rcon.CommandAsync("127.0.0.1", RconPort, _password, "list", CancellationToken.None);
            if (PlayerList.TryParse(_redactor.Clean(text), out var online, out var max, out var names))
            {
                _playerLine = online + " / " + max;
                PlayerMetric.Value = _playerLine;
                Replace(_players, names.ToList());
            }
        }
        catch
        {
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
