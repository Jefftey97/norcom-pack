using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class Admin140Tests
{
    private const string SparkText = """
        TPS from last 5s, 10s, 1m, 5m, 15m:
          20.0, 19.8, 19.5, 19.2, 18.9
        Tick durations (min/med/95%ile/max ms) from last 10s, 1m:
          4.1 / 12.0 / 28.4 / 41.2;  3.8 / 11.2 / 30.1 / 55.0
        CPU usage from last 10s, 1m, 15m:
          22.0%, 21.4%, 19.0% (system)
          8.1%, 7.4%, 6.2% (process)
        Memory usage:
          3.2 GB / 8.0 GB (40%)
        G1 Young Generation collector:
          8.21 ms avg, 42 total collections
          15s avg frequency
        G1 Old Generation collector:
          40.5 ms avg, 2 total collections
          12m 4s avg frequency
        Average Pings (min/med/95%ile/max ms) from now, last 15m:
          12 / 40 / 88 / 140;  10 / 36 / 90 / 150
        Entities: 842
        Loaded chunks: 1,204
        """;

    [Fact]
    public void SparkReport_reads_dashboard_fields()
    {
        var report = SparkReportParser.Parse(SparkText);
        Assert.Equal("19.8 / 19.5 / 19.2 / 18.9", report.TpsLine);
        Assert.Equal("3.8", report.MsptMin);
        Assert.Equal("11.2", report.MsptMedian);
        Assert.Equal("30.1", report.MsptP95);
        Assert.Equal("55.0", report.MsptMax);
        Assert.True(report.MsptAbove40);
        Assert.Equal("7.4", report.CpuProcess1m);
        Assert.Equal("21.4", report.CpuSystem1m);
        Assert.Equal("3.2 GB", report.HeapUsed);
        Assert.Equal("8.0 GB", report.HeapMax);
        Assert.Equal("42", report.Young?.Count);
        Assert.Equal("8.21 ms", report.Young?.AvgPause);
        Assert.Equal("15s", report.Young?.Frequency);
        Assert.Equal("2", report.Old?.Count);
        Assert.Equal("40", report.PingMedian);
        Assert.Equal("88", report.PingP95);
        Assert.Equal("842", report.Entities);
        Assert.Equal("1,204", report.Chunks);
    }

    [Fact]
    public void Backup_refuses_a_live_world_and_dedupes_the_maintenance_window()
    {
        var quiet = new DateTime(2026, 11, 6, 15, 0, 0);
        var window = new DateTime(2026, 11, 6, 4, 0, 30);
        var edge = new DateTime(2026, 11, 6, 4, 1, 59);
        var after = new DateTime(2026, 11, 6, 4, 2, 0);

        Assert.Equal(BackupStep.Zip, BackupPolicy.Decide(javaWriting: false, canFlush: true, duplicate: false));
        Assert.Equal(BackupStep.FlushThenRecheck, BackupPolicy.Decide(javaWriting: true, canFlush: true, duplicate: false));
        Assert.Equal(BackupStep.Refuse, BackupPolicy.Decide(javaWriting: true, canFlush: false, duplicate: false));
        Assert.Equal(BackupStep.Skip, BackupPolicy.Decide(javaWriting: true, canFlush: true, duplicate: true));
        Assert.Equal(BackupStep.Refuse, BackupPolicy.AfterFlush(stillWriting: true, duplicate: false));
        Assert.Equal(BackupStep.Zip, BackupPolicy.AfterFlush(stillWriting: false, duplicate: false));
        Assert.True(BackupPolicy.IsDuplicate(window, false, false));
        Assert.True(BackupPolicy.IsDuplicate(edge, false, false));
        Assert.True(BackupPolicy.IsDuplicate(quiet, freshAutoZip: true, maintenanceFlag: false));
        Assert.True(BackupPolicy.IsDuplicate(quiet, false, maintenanceFlag: true));
        Assert.False(BackupPolicy.IsDuplicate(after, false, false));
        Assert.False(BackupPolicy.IsDuplicate(quiet, false, false));
        Assert.Contains("No zip", BackupPolicy.FlushStillLiveMessage("session.lock"), StringComparison.Ordinal);
        Assert.Contains("Stop", BackupPolicy.NoFlushPathMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Fresh_auto_zip_and_maintenance_flag_are_detected()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        var auto = Path.Combine(root, "backups", "auto");
        Directory.CreateDirectory(auto);
        var zip = Path.Combine(auto, "world-auto.zip");
        File.WriteAllText(zip, "zip");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(zip, now.AddMinutes(-1));
        Assert.True(BackupSignals.FreshAutoZip(root, now));
        File.SetLastWriteTimeUtc(zip, now.AddMinutes(-3));
        Assert.False(BackupSignals.FreshAutoZip(root, now));

        var flag = Path.Combine(root, "maintenance.flag");
        File.WriteAllText(flag, "maint");
        File.SetLastWriteTimeUtc(flag, now.AddSeconds(-30));
        Assert.True(BackupSignals.MaintenanceFlag(root, now));
        File.SetLastWriteTimeUtc(flag, now.AddHours(-20));
        Assert.False(BackupSignals.MaintenanceFlag(root, now));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Zip_is_not_created_while_session_lock_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        File.WriteAllText(Path.Combine(root, "world", "level.dat"), "world");
        File.WriteAllText(Path.Combine(root, "world", "session.lock"), "lock");
        var ex = Assert.Throws<InvalidOperationException>(() => BackupArchive.CreateZip(root));
        Assert.Contains("not zipped", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, "backups")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Quiet_world_zips_without_session_lock()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        File.WriteAllText(Path.Combine(root, "world", "level.dat"), "world");
        File.WriteAllText(Path.Combine(root, "world", "session.lock"), "stale");
        File.Delete(Path.Combine(root, "world", "session.lock"));
        var zip = BackupArchive.CreateZip(root);
        Assert.True(File.Exists(zip));
        Assert.EndsWith(".zip", zip, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Swap_and_disk_baselines_are_health_critical()
    {
        long gb = 1024L * 1024 * 1024;
        Assert.True(HostHealth.SwapCritical(35 * gb, 48 * gb));
        Assert.False(HostHealth.SwapCritical(2 * gb, 48 * gb));
        Assert.True(HostHealth.DiskHigh(83));
        Assert.False(HostHealth.DiskHigh(79));
        Assert.Equal("disk high, swap critical", ServerHealth.Score(false, true, true, false));
    }

    [Fact]
    public void Meminfo_reads_ram_and_swap()
    {
        const string info = """
            MemTotal:       32768000 kB
            MemAvailable:   10485760 kB
            SwapTotal:      50331648 kB
            SwapFree:       13631488 kB
            """;
        var ram = HostSnapshot.MemoryFromMemInfo(info);
        var swap = HostSnapshot.SwapFromMemInfo(info);
        Assert.True(ram.Used > 0);
        Assert.True(HostHealth.SwapCritical(swap.Used, swap.Total));
        Assert.Contains("GB", swap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_reads_latest_log_while_the_file_is_open()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var log = Path.Combine(root, "logs", "latest.log");
        File.WriteAllText(log, "start\nMSPT 12.5 tick\n");
        using var hold = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var overview = ServerOverviewReader.Read(root);
        Assert.Contains("MSPT 12.5", overview.TickLine, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Crash_inbox_names_the_last_fault()
    {
        Assert.Equal("Description: Watching server", CrashReports.FaultLine("----\nDescription: Watching server\n"));
    }

    [Fact]
    public void Settings_upsert_rejects_simulation_distance_and_passwords()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServerProperties.Upsert("simulation-distance=10\n", new Dictionary<string, string> { ["simulation-distance"] = "6" }));
        Assert.Throws<InvalidOperationException>(() =>
            ServerProperties.Upsert("", new Dictionary<string, string> { ["rcon.password"] = "secret" }));
    }
}
