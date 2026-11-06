using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class AutoFixViewModel : ObservableObject
{
    private readonly AdminSettings _settings;
    private readonly Func<string> _serverPath;
    private readonly Func<bool> _processAlive;
    private readonly SecretRedactor _redactor;
    private readonly Action _save;
    private readonly HashSet<string> _seenCrashes = new(StringComparer.OrdinalIgnoreCase);

    public AutoFixViewModel(AdminSettings settings, Func<string> serverPath, Func<bool> processAlive, SecretRedactor redactor, Action save)
    {
        _settings = settings;
        _serverPath = serverPath;
        _processAlive = processAlive;
        _redactor = redactor;
        _save = save;
        var options = settings.AutoFix;
        FrozenServerRestart = options.FrozenServerRestart;
        TunnelReconnect = options.TunnelReconnect;
        BadModRollback = options.BadModRollback;
        DiskLogCleanup = options.DiskLogCleanup;
        BrokenConfigRestore = options.BrokenConfigRestore;
        CrashAlerts = options.CrashAlerts;
    }

    [ObservableProperty] private bool frozenServerRestart;
    [ObservableProperty] private bool tunnelReconnect;
    [ObservableProperty] private bool badModRollback;
    [ObservableProperty] private bool diskLogCleanup;
    [ObservableProperty] private bool brokenConfigRestore;
    [ObservableProperty] private bool crashAlerts;
    [ObservableProperty] private string status = "Each toggle is independent. Frozen only posts a notice. Restart stays typed.";

    [RelayCommand]
    private void RunNow() => Tick(announce: true);

    public void Tick(bool announce)
    {
        var server = _serverPath();
        if (!Directory.Exists(server))
        {
            if (announce)
            {
                Status = "Server folder was not found, so auto-fix has nothing to touch.";
            }

            return;
        }

        var notes = new List<string>();
        if (FrozenServerRestart)
        {
            var log = Path.Combine(server, "logs", "latest.log");
            DateTime? last = File.Exists(log) ? File.GetLastWriteTimeUtc(log) : null;
            if (AutoFix.ShouldRestartFrozen(true, _processAlive(), last, DateTime.UtcNow, TimeSpan.FromMinutes(_settings.AutoFix.FrozenAfterMinutes)))
            {
                notes.Add("Server log looks frozen. Use soft restart and type RESTART.");
            }
        }

        if (DiskLogCleanup)
        {
            var count = AutoFix.CleanupCandidates(server, DateTime.UtcNow, _settings.AutoFix.KeepLogsDays).Count;
            AutoFix.DeleteOldLogs(server, DateTime.UtcNow, _settings.AutoFix.KeepLogsDays);
            if (count > 0)
            {
                notes.Add("Removed " + count + " old log files. latest.log was kept.");
            }
        }

        if (BrokenConfigRestore)
        {
            var backupRoot = Path.Combine(server, "config", ".norcom-backup");
            if (Directory.Exists(backupRoot))
            {
                foreach (var item in Directory.EnumerateFiles(backupRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(backupRoot, item);
                    if (AutoFix.TryRestoreBrokenConfig(Path.Combine(server, "config", relative), item))
                    {
                        notes.Add("Restored " + relative);
                    }
                }
            }
        }

        if (BadModRollback)
        {
            var crashes = Path.Combine(server, "crash-reports");
            if (Directory.Exists(crashes))
            {
                var newest = Directory.EnumerateFiles(crashes).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (newest != null)
                {
                    var rolled = AutoFix.RollbackBadMod(server, File.ReadAllText(newest));
                    if (rolled != null)
                    {
                        notes.Add("Quarantined " + rolled + ". " + NorcomPin.AskServer);
                    }
                }
            }
        }

        if (CrashAlerts)
        {
            foreach (var name in AutoFix.NewCrashReports(server, _seenCrashes))
            {
                notes.Add("New crash report: " + name);
            }
        }

        if (TunnelReconnect)
        {
            var playit = new[] { "playit.exe", Path.Combine("tunnel", "playit.exe") }
                .Select(name => Path.Combine(server, name))
                .FirstOrDefault(File.Exists);
            notes.Add(playit == null
                ? "Tunnel check is on, and no playit.exe was found next to the server."
                : "Tunnel program is present at " + Path.GetFileName(playit) + ".");
        }

        if (notes.Count == 0)
        {
            if (announce)
            {
                Status = "Auto-fix ran. Nothing needed attention.";
            }

            return;
        }

        Status = string.Join(" ", notes);
        _redactor.WriteLog(Status);
    }

    partial void OnFrozenServerRestartChanged(bool value) => Store();
    partial void OnTunnelReconnectChanged(bool value) => Store();
    partial void OnBadModRollbackChanged(bool value) => Store();
    partial void OnDiskLogCleanupChanged(bool value) => Store();
    partial void OnBrokenConfigRestoreChanged(bool value) => Store();
    partial void OnCrashAlertsChanged(bool value) => Store();

    private void Store()
    {
        _settings.AutoFix.FrozenServerRestart = FrozenServerRestart;
        _settings.AutoFix.TunnelReconnect = TunnelReconnect;
        _settings.AutoFix.BadModRollback = BadModRollback;
        _settings.AutoFix.DiskLogCleanup = DiskLogCleanup;
        _settings.AutoFix.BrokenConfigRestore = BrokenConfigRestore;
        _settings.AutoFix.CrashAlerts = CrashAlerts;
        _save();
    }
}
