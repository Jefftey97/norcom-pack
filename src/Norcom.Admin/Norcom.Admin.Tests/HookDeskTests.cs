using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class HookDeskTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Backup_age_prefers_norcom_and_marks_stale_after_36_hours()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine(".norcom", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T17:00:00Z","source":"nightly","lastBackupPath":"G:\\MinecraftServer\\backups\\auto\\world.zip","lastBackupMtime":"2026-11-06T07:00:00Z","ageHours":3,"sizeBytes":1073741824,"status":"ok"}
                """);
            Write(root, Path.Combine("admin", "hooks", "backup-age.json"), """
                {"schema":1,"ageHours":20,"status":"stale"}
                """);

            var fresh = HookDesk.ReadBackup(root, Now);
            Assert.True(fresh.Live);
            Assert.Equal("3h ago", fresh.AgeText);
            Assert.Equal("OK", fresh.Chip);
            Assert.False(fresh.Alert);
            Assert.Contains(".norcom", fresh.Hint, StringComparison.Ordinal);
            Assert.Contains("world.zip", fresh.Hint, StringComparison.Ordinal);
            Assert.Contains("1.0 GB", fresh.Hint, StringComparison.Ordinal);

            Write(root, Path.Combine(".norcom", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T17:00:00Z","ageHours":36,"status":"ok"}
                """);
            Assert.Equal("OK", HookDesk.ReadBackup(root, Now).Chip);

            Write(root, Path.Combine(".norcom", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T17:00:00Z","ageHours":36.1,"status":"ok"}
                """);
            var stale = HookDesk.ReadBackup(root, Now);
            Assert.Equal("STALE", stale.Chip);
            Assert.Equal("36h ago", stale.AgeText);
            Assert.True(stale.Alert);

            Write(root, Path.Combine(".norcom", "backup-age.json"), """
                {"schema":1,"status":"missing","ageHours":1}
                """);
            var missing = HookDesk.ReadBackup(root, Now);
            Assert.True(missing.Live);
            Assert.Equal("MISSING", missing.Chip);
            Assert.Equal("—", missing.AgeText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Backup_mirror_and_zip_mtime_fill_in_when_the_hook_is_absent()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine("admin", "hooks", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T17:00:00Z","ageHours":1,"status":"ok"}
                """);
            var mirror = HookDesk.ReadBackup(root, Now);
            Assert.Equal("1h ago", mirror.AgeText);
            Assert.Equal("OK", mirror.Chip);
            Assert.Contains("admin\\hooks", mirror.Hint, StringComparison.Ordinal);

            Write(root, Path.Combine("admin", "hooks", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T15:00:00Z","ageHours":1,"status":"ok"}
                """);
            Assert.Equal("3h ago", HookDesk.ReadBackup(root, Now).AgeText);

            File.Delete(Path.Combine(root, "admin", "hooks", "backup-age.json"));
            var zipDir = Path.Combine(root, "backups", "auto");
            Directory.CreateDirectory(zipDir);
            var zip = Path.Combine(zipDir, "nightly.zip");
            File.WriteAllText(zip, "zip");
            File.SetLastWriteTimeUtc(zip, Now.AddHours(-3).AddMinutes(-10));
            var fromZip = HookDesk.ReadBackup(root, Now);
            Assert.Equal("3h ago", fromZip.AgeText);
            Assert.Equal("OK", fromZip.Chip);
            Assert.Contains("nightly.zip", fromZip.Hint, StringComparison.Ordinal);

            File.SetLastWriteTimeUtc(zip, Now.AddHours(-40).AddMinutes(-2));
            var old = HookDesk.ReadBackup(root, Now);
            Assert.Equal("STALE", old.Chip);
            Assert.Equal("40h ago", old.AgeText);

            File.Delete(zip);
            Write(root, Path.Combine("admin", "state.json"), """
                {"backupAgeMinutes":90,"lastBackupUtc":"2026-11-06T16:00:00Z"}
                """);
            var fromState = HookDesk.ReadBackup(root, Now);
            Assert.Equal("1h ago", fromState.AgeText);
            Assert.Equal("OK", fromState.Chip);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Broken_hook_falls_through_and_a_missing_folder_stays_a_dash()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine(".norcom", "backup-age.json"), "{not json");
            Write(root, Path.Combine("admin", "hooks", "backup-age.json"), """
                {"ageHours":2,"status":"ok","generatedAtUtc":"2026-11-06T17:00:00Z"}
                """);
            Assert.Equal("2h ago", HookDesk.ReadBackup(root, Now).AgeText);

            var empty = Temp();
            try
            {
                var reading = HookDesk.ReadBackup(empty, Now);
                Assert.False(reading.Live);
                Assert.Equal("—", reading.AgeText);
                Assert.Equal("", reading.Chip);
            }
            finally
            {
                Directory.Delete(empty, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void State_json_is_the_playit_and_heap_chip_source()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine("admin", "state.json"), """
                {"playitUp":true,"heapUsedMb":5431.4,"heapMaxMb":8192,"metricsUtc":"2026-11-06T16:58:00Z","lastTunnelAddress":"norcom.ply.gg"}
                """);
            Write(root, Path.Combine(".norcom", "runtime-watch.json"), """
                {"schema":1,"oneLiner":"playit down · heap 1 / 1 MB","heapOk":false,"playitUp":false}
                """);
            File.WriteAllText(Path.Combine(root, "start.bat"), "java -Xms8G -Xmx8G -jar fabric-server-launch.jar nogui\r\n");

            var live = HookDesk.ReadHeapPlayit(root, Now, @"java -Xms8192M -Xmx8g -jar G:\MinecraftServer\fabric-server-launch.jar nogui");
            Assert.True(live.PlayitLive);
            Assert.Equal("PLAYIT UP", live.PlayitChip);
            Assert.False(live.PlayitAlert);
            Assert.Equal("norcom.ply.gg", live.Tunnel);
            Assert.True(live.HeapLive);
            Assert.Equal("5431 / 8192 MB", live.HeapText);
            Assert.Contains("metrics 2m ago", live.Hint, StringComparison.Ordinal);
            Assert.Contains("target -Xms8G/-Xmx8G", live.Hint, StringComparison.Ordinal);
            Assert.Equal("playit down · heap 1 / 1 MB", live.OneLiner);
            Assert.Equal("-Xms8G/-Xmx8G", live.HeapFlags);

            Write(root, Path.Combine("admin", "state.json"), """
                {"playitUp":"false","heapUsedMb":"100","heapMaxMb":"8192","metricsUtc":"2026-11-06T16:58:00Z"}
                """);
            var down = HookDesk.ReadHeapPlayit(root, Now);
            Assert.Equal("PLAYIT DOWN", down.PlayitChip);
            Assert.True(down.PlayitAlert);
            Assert.Equal("100 / 8192 MB", down.HeapText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_state_keeps_heap_and_playit_as_a_dash_but_keeps_a_one_liner()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine("admin", "hooks", "runtime-watch.json"), """
                {"oneLiner":"token=abcdef heap still up","playitUp":true,"heapXms":"8G","heapXmx":"8G"}
                """);
            var reading = HookDesk.ReadHeapPlayit(root, Now);
            Assert.False(reading.PlayitLive);
            Assert.Equal("", reading.PlayitChip);
            Assert.False(reading.HeapLive);
            Assert.Equal("—", reading.HeapText);
            Assert.Contains("token=(hidden)", reading.OneLiner, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdef", reading.OneLiner, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Dashboard_reads_backup_and_state_together()
    {
        var root = Temp();
        try
        {
            Write(root, Path.Combine(".norcom", "backup-age.json"), """
                {"schema":1,"generatedAtUtc":"2026-11-06T17:00:00Z","ageHours":3,"status":"ok","lastBackupPath":"G:\\MinecraftServer\\backups\\auto\\world.zip"}
                """);
            Write(root, Path.Combine("admin", "state.json"), """
                {"playitUp":true,"heapUsedMb":5431,"heapMaxMb":8192,"metricsUtc":"2026-11-06T16:58:00Z"}
                """);
            var backup = HookDesk.ReadBackup(root, Now);
            var heap = HookDesk.ReadHeapPlayit(root, Now);
            Assert.Equal("3h ago", backup.AgeText);
            Assert.Equal("OK", backup.Chip);
            Assert.Equal("PLAYIT UP", heap.PlayitChip);
            Assert.Equal("5431 / 8192 MB", heap.HeapText);
            Assert.Equal("", heap.OneLiner);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relative, string json)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
