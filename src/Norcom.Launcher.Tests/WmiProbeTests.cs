using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

// Regression: ManagementObjectSearcher.Get has two public overloads, so the
// reflection lookup GetMethod("Get") threw AmbiguousMatchException and every
// WMI probe (game process, RAM, graphics) silently returned nothing.
public class WmiProbeTests
{
    private const string SearcherType = "System.Management.ManagementObjectSearcher, System.Management";

    private static string LogText() => File.Exists(NorcomPaths.LogPath) ? File.ReadAllText(NorcomPaths.LogPath) : "";

    private static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    [Fact]
    public void HostRamProbeReadsRealWmi()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.NotNull(Type.GetType(SearcherType));
        Assert.True(HardwareProbe.TryHostRamBytes() > 0);
    }

    [Fact]
    public void PrismGameProbeQueriesWmiWithoutReflectionFailure()
    {
        Assert.NotNull(Type.GetType(SearcherType));
        string dir = Path.Combine(Path.GetTempPath(), "norcom-wmi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            int before = Count(LogText(), "prism game probe");
            new LivePrismGameSignals(dir, dir).TryGameProcessId(null);
            Assert.Equal(before, Count(LogText(), "prism game probe"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GraphicsProbeQueriesWmiWithoutReflectionFailure()
    {
        Assert.NotNull(Type.GetType(SearcherType));
        var saved = HardwareProbe.Reader;
        HardwareProbe.Reader = null;
        try
        {
            int before = Count(LogText(), "graphics probe");
            HardwareProbe.TryRead();
            Assert.Equal(before, Count(LogText(), "graphics probe"));
        }
        finally { HardwareProbe.Reader = saved; }
    }

    [Fact]
    public void SearcherGetLookupIsUnambiguousAndReadsThisProcess()
    {
        Type searcher = Type.GetType(SearcherType)!;
        Assert.Throws<System.Reflection.AmbiguousMatchException>(() => searcher.GetMethod("Get"));
        Assert.NotNull(WmiQuery.ParameterlessGet(searcher));
        int pid = Environment.ProcessId;
        var rows = WmiQuery.Rows("SELECT ProcessId, Name FROM Win32_Process WHERE ProcessId=" + pid, "ProcessId", "Name");
        Assert.NotNull(rows);
        var row = Assert.Single(rows!);
        Assert.Equal(pid, WmiQuery.Number(row, "ProcessId"));
        Assert.False(string.IsNullOrWhiteSpace(WmiQuery.Text(row, "Name")));
    }
}