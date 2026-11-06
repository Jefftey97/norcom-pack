using System.Globalization;
using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public sealed record GcStat(string Name, string Count, string AvgPause, string Frequency);

public sealed record SparkReport(
    string? Tps10s,
    string? Tps1m,
    string? Tps5m,
    string? Tps15m,
    string? MsptMin,
    string? MsptMedian,
    string? MsptP95,
    string? MsptMax,
    string? CpuProcess1m,
    string? CpuSystem1m,
    string? HeapUsed,
    string? HeapMax,
    GcStat? Young,
    GcStat? Old,
    string? PingMedian,
    string? PingP95,
    string? Entities,
    string? Chunks)
{
    public static SparkReport Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    public bool MsptAbove40 =>
        Above(MsptP95, 40) || Above(MsptMax, 40) || Above(MsptMedian, 40);

    public string TpsLine => Join(" / ", Tps10s, Tps1m, Tps5m, Tps15m);

    public string MsptLine =>
        MsptMin == null && MsptMedian == null
            ? "—"
            : "min " + Show(MsptMin) + " · med " + Show(MsptMedian) + " · p95 " + Show(MsptP95) + " · max " + Show(MsptMax);

    public string CpuLine =>
        CpuProcess1m == null && CpuSystem1m == null
            ? "—"
            : "proc " + Show(CpuProcess1m) + "% · sys " + Show(CpuSystem1m) + "%";

    public string HeapLine => HeapUsed == null ? "— / 8 GB" : HeapUsed + " / " + (HeapMax ?? "8 GB");

    public string PingLine => PingMedian == null ? "—" : "med " + PingMedian + " · p95 " + Show(PingP95) + " ms";

    public string WorldLine =>
        Entities == null && Chunks == null
            ? "—"
            : "ents " + Show(Entities) + " · chunks " + Show(Chunks);

    public static string GcLine(GcStat? stat) =>
        stat == null ? "—" : stat.Count + " · " + stat.AvgPause + " · " + stat.Frequency;

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Join(string separator, params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return present.Length == 0 ? "—" : string.Join(separator, present);
    }

    private static bool Above(string? value, double limit) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > limit;
}

public static class SparkReportParser
{
    public static SparkReport Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return SparkReport.Empty;
        }

        var text = Regex.Replace(raw, "§.", "");
        text = text.Replace("*", "");

        string? tps10 = null, tps1 = null, tps5 = null, tps15 = null;
        var tps = Regex.Match(text, @"TPS from last[^:\r\n]*:\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)\s*,\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
        if (tps.Success)
        {
            tps10 = tps.Groups[2].Value;
            tps1 = tps.Groups[3].Value;
            tps5 = tps.Groups[4].Value;
            tps15 = tps.Groups[5].Value;
        }

        string? min = null, med = null, p95 = null, max = null;
        var tick = Regex.Match(
            text,
            @"Tick durations[\s\S]{0,180}?([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)(?:\s*;\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?))?",
            RegexOptions.IgnoreCase);
        if (tick.Success)
        {
            var use1m = tick.Groups[5].Success;
            min = tick.Groups[use1m ? 5 : 1].Value;
            med = tick.Groups[use1m ? 6 : 2].Value;
            p95 = tick.Groups[use1m ? 7 : 3].Value;
            max = tick.Groups[use1m ? 8 : 4].Value;
        }

        var system = Regex.Match(text, @"([0-9]+(?:\.[0-9]+)?)%\s*,\s*([0-9]+(?:\.[0-9]+)?)%\s*,\s*([0-9]+(?:\.[0-9]+)?)%\s*\(system\)", RegexOptions.IgnoreCase);
        var process = Regex.Match(text, @"([0-9]+(?:\.[0-9]+)?)%\s*,\s*([0-9]+(?:\.[0-9]+)?)%\s*,\s*([0-9]+(?:\.[0-9]+)?)%\s*\(process\)", RegexOptions.IgnoreCase);
        var memory = Regex.Match(text, @"Memory usage:[\s\S]{0,100}?([0-9]+(?:\.[0-9]+)?)\s*(KB|MB|GB|TB)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*(KB|MB|GB|TB)", RegexOptions.IgnoreCase);
        var ping = Regex.Match(text, @"Average Pings[\s\S]{0,120}?([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*/\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
        var entities = Regex.Match(text, @"(?i)(?:total\s+)?entities[:\s]+([0-9][0-9,]*)");
        var chunks = Regex.Match(text, @"(?i)(?:loaded\s+chunks|chunks(?:\s+loaded)?)[:\s]+([0-9][0-9,]*)");

        return new SparkReport(
            tps10,
            tps1,
            tps5,
            tps15,
            min,
            med,
            p95,
            max,
            process.Success ? process.Groups[2].Value : null,
            system.Success ? system.Groups[2].Value : null,
            memory.Success ? memory.Groups[1].Value + " " + memory.Groups[2].Value.ToUpperInvariant() : null,
            memory.Success ? memory.Groups[3].Value + " " + memory.Groups[4].Value.ToUpperInvariant() : null,
            FindGc(text, young: true),
            FindGc(text, young: false),
            ping.Success ? ping.Groups[2].Value : null,
            ping.Success ? ping.Groups[3].Value : null,
            entities.Success ? entities.Groups[1].Value : null,
            chunks.Success ? chunks.Groups[1].Value : null);
    }

    public static string FormatPercent(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static GcStat? FindGc(string text, bool young)
    {
        foreach (Match match in Regex.Matches(
            text,
            @"([A-Za-z0-9][A-Za-z0-9 ._-]{0,40}?) collector:\s*([0-9]+(?:\.[0-9]+)?)\s*ms avg\s*,\s*([0-9][0-9,]*)\s*total collections\s*([^\r\n]+?)\s*avg frequency",
            RegexOptions.IgnoreCase))
        {
            var name = match.Groups[1].Value.Trim();
            var isYoung = name.Contains("young", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("minor", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("scavenge", StringComparison.OrdinalIgnoreCase);
            var isOld = name.Contains("old", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("major", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("tenured", StringComparison.OrdinalIgnoreCase);
            if (young ? isYoung : isOld)
            {
                return new GcStat(name, match.Groups[3].Value, match.Groups[2].Value + " ms", match.Groups[4].Value.Trim());
            }
        }

        return null;
    }
}
