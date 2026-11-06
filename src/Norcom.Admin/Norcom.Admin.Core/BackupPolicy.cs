namespace Norcom.Admin.Core;

public enum BackupStep
{
    Zip,
    FlushThenRecheck,
    Refuse,
    Skip
}

public static class BackupPolicy
{
    public static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(2);

    public static bool InAutoWindow(DateTime denverLocal) =>
        denverLocal.Hour == 4 && denverLocal.Minute is 0 or 1;

    public static BackupStep Decide(bool javaWriting, bool canFlush, bool duplicate)
    {
        if (duplicate)
        {
            return BackupStep.Skip;
        }

        if (!javaWriting)
        {
            return BackupStep.Zip;
        }

        return canFlush ? BackupStep.FlushThenRecheck : BackupStep.Refuse;
    }

    public static BackupStep AfterFlush(bool stillWriting, bool duplicate)
    {
        if (duplicate)
        {
            return BackupStep.Skip;
        }

        return stillWriting ? BackupStep.Refuse : BackupStep.Zip;
    }

    public static bool IsDuplicate(DateTime denverLocal, bool freshAutoZip, bool maintenanceFlag) =>
        InAutoWindow(denverLocal) || freshAutoZip || maintenanceFlag;

    public static bool FlagCoversWindow(DateTime flagWrittenUtc, DateTime utcNow)
    {
        if (utcNow < flagWrittenUtc)
        {
            return false;
        }

        if (utcNow - flagWrittenUtc <= FreshWindow)
        {
            return true;
        }

        var flagLocal = DenverClock.Convert(flagWrittenUtc);
        var nowLocal = DenverClock.Convert(utcNow);
        return flagLocal.Date == nowLocal.Date &&
               InAutoWindow(flagLocal) &&
               nowLocal.TimeOfDay < new TimeSpan(4, 3, 0);
    }

    public const string LiveWorldMessage =
        "Backup unavailable while the world is live; use the nightly auto zip.";

    public static string RefuseMessage(string why) => LiveWorldMessage + " (" + why + ")";

    public static string FlushStillLiveMessage(string why) => LiveWorldMessage + " (" + why + ")";

    public const string SkipMessage =
        "A second zip was skipped. The 04:00–04:01 Denver backup, a fresh auto zip, or maintenance.flag already covers this window.";

    public const string NoFlushPathMessage = LiveWorldMessage;
}

public static class BackupSignals
{
    public static bool FreshAutoZip(string serverDirectory, DateTime utcNow)
    {
        var auto = Path.Combine(serverDirectory, "backups", "auto");
        if (!Directory.Exists(auto))
        {
            return false;
        }

        foreach (var file in Directory.EnumerateFiles(auto, "*.zip", SearchOption.AllDirectories))
        {
            var age = utcNow - File.GetLastWriteTimeUtc(file);
            if (age >= TimeSpan.Zero && age <= BackupPolicy.FreshWindow)
            {
                return true;
            }
        }

        return false;
    }

    public static bool MaintenanceFlag(string serverDirectory, DateTime utcNow)
    {
        foreach (var relative in new[] { "maintenance.flag", Path.Combine("backups", "maintenance.flag"), Path.Combine("backups", "auto", "maintenance.flag") })
        {
            var path = Path.Combine(serverDirectory, relative);
            if (!File.Exists(path))
            {
                continue;
            }

            if (BackupPolicy.FlagCoversWindow(File.GetLastWriteTimeUtc(path), utcNow))
            {
                return true;
            }
        }

        return false;
    }
}

public static class DenverClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateTime Now() => Convert(DateTime.UtcNow);

    public static DateTime Convert(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Denver", "Mountain Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
