using System.Globalization;
using System.Runtime.InteropServices;

namespace Norcom.Admin.Core;

public sealed record BytePair(long Used, long Total)
{
    public double Percent => Total <= 0 ? 0 : Used * 100d / Total;

    public string Text
    {
        get
        {
            if (Total <= 0)
            {
                return "—";
            }

            if (Total >= 1L << 30)
            {
                return Gb(Used) + " / " + Gb(Total) + " GB";
            }

            return (Used / 1048576d).ToString("0", CultureInfo.InvariantCulture) + " / " +
                   (Total / 1048576d).ToString("0", CultureInfo.InvariantCulture) + " MB";
        }
    }

    private static string Gb(long bytes) => (bytes / 1073741824d).ToString("0.0", CultureInfo.InvariantCulture);
}

public sealed record DriveUsage(string Label, long Used, long Total)
{
    public double Percent => Total <= 0 ? 0 : Used * 100d / Total;

    public string Text => Total <= 0 ? "—" : Percent.ToString("0", CultureInfo.InvariantCulture) + "% used";
}

public static class HostHealth
{
    public const double DiskHighPercent = 80;
    public const double SwapCriticalPercent = 50;

    public static bool DiskHigh(double percentUsed) => percentUsed >= DiskHighPercent;

    public static bool SwapCritical(long used, long total) => total > 0 && used * 100d / total >= SwapCriticalPercent;
}

public static class HostSnapshot
{
    public static BytePair MemoryFromMemInfo(string meminfo)
    {
        var total = Kib(meminfo, "MemTotal");
        var available = Kib(meminfo, "MemAvailable");
        if (available <= 0)
        {
            available = Kib(meminfo, "MemFree");
        }

        var used = Math.Max(0, total - available);
        return new BytePair(used, total);
    }

    public static BytePair SwapFromMemInfo(string meminfo)
    {
        var total = Kib(meminfo, "SwapTotal");
        var free = Kib(meminfo, "SwapFree");
        return new BytePair(Math.Max(0, total - free), total);
    }

    public static BytePair ReadRam() => OperatingSystem.IsWindows() ? ReadWindows().Ram : ReadProc().Ram;

    public static BytePair ReadSwap() => OperatingSystem.IsWindows() ? ReadWindows().Swap : ReadProc().Swap;

    public static DriveUsage ReadDrive(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new DriveUsage("Disk", 0, 0);
            }

            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return new DriveUsage("Disk", 0, 0);
            }

            var drive = new DriveInfo(root);
            var used = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
            var label = root.TrimEnd('\\', '/');
            if (label.Length == 0)
            {
                label = root;
            }

            return new DriveUsage(label, used, drive.TotalSize);
        }
        catch
        {
            return new DriveUsage("Disk", 0, 0);
        }
    }

    private static (BytePair Ram, BytePair Swap) ReadProc()
    {
        const string path = "/proc/meminfo";
        if (!File.Exists(path))
        {
            return (new BytePair(0, 0), new BytePair(0, 0));
        }

        var text = File.ReadAllText(path);
        return (MemoryFromMemInfo(text), SwapFromMemInfo(text));
    }

    private static (BytePair Ram, BytePair Swap) ReadWindows()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return (new BytePair(0, 0), new BytePair(0, 0));
        }

        var ramUsed = status.ullTotalPhys - status.ullAvailPhys;
        var commitUsed = status.ullTotalPageFile > status.ullAvailPageFile
            ? status.ullTotalPageFile - status.ullAvailPageFile
            : 0;
        var swapTotal = status.ullTotalPageFile > status.ullTotalPhys
            ? status.ullTotalPageFile - status.ullTotalPhys
            : 0;
        var swapUsed = commitUsed > ramUsed ? commitUsed - ramUsed : 0;
        if (swapUsed > swapTotal)
        {
            swapUsed = swapTotal;
        }

        return (new BytePair((long)ramUsed, (long)status.ullTotalPhys), new BytePair((long)swapUsed, (long)swapTotal));
    }

    private static long Kib(string text, string key)
    {
        var match = RegexCache.Match(text, @"(?im)^" + key + @":\s*([0-9]+)\s*kB");
        return match.Success && long.TryParse(match.Groups[1].Value, out var kib)
            ? kib * 1024
            : 0;
    }

    private static class RegexCache
    {
        public static System.Text.RegularExpressions.Match Match(string text, string pattern) =>
            System.Text.RegularExpressions.Regex.Match(text, pattern);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
