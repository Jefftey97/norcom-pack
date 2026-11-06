using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public sealed record BackupAgeReading(bool Live, string AgeText, string Chip, string Hint, bool Alert)
{
    public static BackupAgeReading Absent { get; } = new(false, "—", "", "No backup-age.json", false);
}

public sealed record HeapPlayitReading(
    bool PlayitLive,
    string PlayitChip,
    bool PlayitAlert,
    string? Tunnel,
    bool HeapLive,
    string HeapText,
    string Hint,
    string OneLiner,
    string HeapFlags)
{
    public static HeapPlayitReading Absent { get; } = new(
        false, "", false, null, false, "—", "No state.json", "", "");
}

/// <summary>
/// Dashboard readings. Backup age prefers .norcom\backup-age.json, then admin\hooks.
/// playit and heap prefer admin\state.json. runtime-watch.json only contributes oneLiner.
/// </summary>
public static class HookDesk
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(36);

    private static readonly JsonDocumentOptions JsonOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Regex XmsFlag = new(@"-Xms(?:=|\s*)(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XmxFlag = new(@"-Xmx(?:=|\s*)(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecretAssign = new(@"(?i)\b(password|token|secret)\s*=\s*\S+", RegexOptions.Compiled);

    public static BackupAgeReading ReadBackup(string? serverDirectory, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return BackupAgeReading.Absent;
        }

        foreach (var (relative, origin) in BackupFiles())
        {
            var path = Path.Combine(serverDirectory, relative);
            if (File.Exists(path) && TryReadBackupFile(path, origin, utcNow, out var reading))
            {
                return reading;
            }
        }

        var fromZip = FromNewestZip(serverDirectory, utcNow);
        if (fromZip.Live)
        {
            return fromZip;
        }

        var state = Path.Combine(serverDirectory, "admin", "state.json");
        if (File.Exists(state) && TryReadBackupFile(state, "state.json", utcNow, out var fromState))
        {
            return fromState;
        }

        return BackupAgeReading.Absent;
    }

    public static HeapPlayitReading ReadHeapPlayit(string? serverDirectory, DateTime utcNow, string? javaCommandLine = null)
    {
        var flags = ReadHeapFlags(serverDirectory, javaCommandLine);
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return HeapPlayitReading.Absent with { HeapFlags = flags };
        }

        var oneLiner = ReadOneLiner(serverDirectory);
        var statePath = Path.Combine(serverDirectory, "admin", "state.json");
        if (!File.Exists(statePath) || !TryReadState(statePath, utcNow, oneLiner, flags, out var reading))
        {
            var hint = File.Exists(statePath) ? "state.json has no playit or heap" : "No state.json";
            if (flags.Length > 0)
            {
                hint = Join(hint, "target " + flags);
            }

            return new HeapPlayitReading(false, "", false, null, false, "—", hint, oneLiner, flags);
        }

        return reading;
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return Math.Max(1, (int)age.TotalMinutes) + "m ago";
        }

        if (age.TotalHours < 48)
        {
            return Math.Max(1, (int)age.TotalHours) + "h ago";
        }

        return Math.Max(2, (int)age.TotalDays) + "d ago";
    }

    private static IEnumerable<(string Relative, string Origin)> BackupFiles()
    {
        yield return (Path.Combine(".norcom", "backup-age.json"), ".norcom");
        yield return (Path.Combine("admin", "hooks", "backup-age.json"), "admin\\hooks");
        yield return (Path.Combine("admin", "backup-age.json"), "admin");
    }

    private static IEnumerable<string> WatchFiles()
    {
        yield return Path.Combine(".norcom", "runtime-watch.json");
        yield return Path.Combine("admin", "hooks", "runtime-watch.json");
        yield return Path.Combine("admin", "metrics-watchdog.json");
    }

    private static bool TryReadBackupFile(string path, string origin, DateTime utcNow, out BackupAgeReading reading)
    {
        reading = BackupAgeReading.Absent;
        if (!TryObject(path, out var root))
        {
            return false;
        }

        var status = Text(root, "status");
        var ageHours = Number(root, "ageHours");
        var ageMinutes = Number(root, "backupAgeMinutes");
        var mtime = Time(root, "lastBackupMtime") ?? Time(root, "lastBackupUtc");
        var backupPath = Text(root, "lastBackupPath");
        var size = Whole(root, "sizeBytes");
        var source = Text(root, "source");
        var generated = Time(root, "generatedAtUtc");
        if (status == null && ageHours == null && ageMinutes == null && mtime == null && backupPath == null)
        {
            return false;
        }

        TimeSpan? age = null;
        if (ageHours is double hours)
        {
            age = TimeSpan.FromHours(hours) + Drift(generated, utcNow);
        }
        else if (mtime is DateTime stamp)
        {
            age = utcNow - stamp;
        }
        else if (ageMinutes is double minutes)
        {
            age = TimeSpan.FromMinutes(minutes) + Drift(generated, utcNow);
        }

        var chip = StatusChip(status, age);
        var ageText = chip == "MISSING" || age == null ? "—" : FormatAge(age.Value);
        var hint = Join(origin, CleanToken(source), FileName(backupPath), size is long bytes ? SizeText(bytes) : "");
        reading = new BackupAgeReading(true, ageText, chip, hint, chip == "STALE");
        return true;
    }

    private static BackupAgeReading FromNewestZip(string serverDirectory, DateTime utcNow)
    {
        var backups = Path.Combine(serverDirectory, "backups");
        if (!Directory.Exists(backups))
        {
            return BackupAgeReading.Absent;
        }

        string? newest = null;
        var newestUtc = DateTime.MinValue;
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(backups, "*.zip", SearchOption.AllDirectories))
            {
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (newest != null && written <= newestUtc)
                {
                    continue;
                }

                newest = file;
                newestUtc = written;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    size = 0;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupAgeReading.Absent;
        }

        if (newest == null)
        {
            return BackupAgeReading.Absent;
        }

        var age = utcNow - newestUtc;
        var chip = age > StaleAfter ? "STALE" : "OK";
        var name = FileName(Path.GetRelativePath(backups, newest));
        return new BackupAgeReading(true, FormatAge(age), chip, Join("backups", name, size > 0 ? SizeText(size) : ""), chip == "STALE");
    }

    private static bool TryReadState(string path, DateTime utcNow, string oneLiner, string flags, out HeapPlayitReading reading)
    {
        reading = HeapPlayitReading.Absent;
        if (!TryObject(path, out var root))
        {
            return false;
        }

        var playit = Bool(root, "playitUp");
        var used = Number(root, "heapUsedMb");
        var max = Number(root, "heapMaxMb");
        if (playit == null && used == null && max == null)
        {
            return false;
        }

        var metrics = Time(root, "metricsUtc");
        var tunnel = CleanHost(Text(root, "lastTunnelAddress"));
        var playitChip = playit == true ? "PLAYIT UP" : playit == false ? "PLAYIT DOWN" : "";
        var heapLive = used != null || max != null;
        var heapText = heapLive ? HeapNumbers(used, max) : "—";
        var hintParts = new List<string>();
        if (metrics is DateTime stamp)
        {
            hintParts.Add("metrics " + FormatAge(utcNow - stamp));
        }
        else if (playit != null || heapLive)
        {
            hintParts.Add("state.json");
        }

        if (!string.IsNullOrWhiteSpace(tunnel))
        {
            hintParts.Add(tunnel);
        }

        if (flags.Length > 0)
        {
            hintParts.Add("target " + flags);
        }

        reading = new HeapPlayitReading(
            playit != null,
            playitChip,
            playit == false,
            tunnel,
            heapLive,
            heapText,
            hintParts.Count == 0 ? "state.json" : string.Join(" · ", hintParts),
            oneLiner,
            flags);
        return true;
    }

    private static string ReadOneLiner(string serverDirectory)
    {
        foreach (var relative in WatchFiles())
        {
            var path = Path.Combine(serverDirectory, relative);
            if (!File.Exists(path) || !TryObject(path, out var root))
            {
                continue;
            }

            var line = Text(root, "oneLiner");
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            line = SecretAssign.Replace(line.Trim(), "$1=(hidden)");
            return line.Length > 400 ? line[..400] : line;
        }

        return "";
    }

    private static string ReadHeapFlags(string? serverDirectory, string? javaCommandLine)
    {
        var live = FormatHeap(javaCommandLine);
        if (live.Length > 0)
        {
            return live;
        }

        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return "";
        }

        foreach (var name in new[] { "start.bat", "run.bat", "start.ps1" })
        {
            var path = Path.Combine(serverDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var flags = FormatHeap(SharedFile.ReadText(path));
                if (flags.Length > 0)
                {
                    return flags;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return "";
    }

    private static string FormatHeap(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var ms = XmsFlag.Match(text);
        var mx = XmxFlag.Match(text);
        if (!ms.Success && !mx.Success)
        {
            return "";
        }

        var xms = ms.Success ? CanonHeap(ms.Groups[1].Value) : null;
        var xmx = mx.Success ? CanonHeap(mx.Groups[1].Value) : null;
        if (xms == null && xmx == null)
        {
            return "";
        }

        return "-Xms" + (xms ?? "—") + "/-Xmx" + (xmx ?? "—");
    }

    private static string? CanonHeap(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim().Trim('"', '\'', ',');
        if (text.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..].TrimStart('=', ' ');
        }

        var match = Regex.Match(text, @"^(?<n>\d+(?:\.\d+)?)\s*(?<u>[gGmMkK])?[bB]?$");
        if (!match.Success)
        {
            return text;
        }

        var amount = double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var gb = match.Groups["u"].Value.ToUpperInvariant() switch
        {
            "G" => amount,
            "M" => amount / 1024d,
            "K" => amount / 1024d / 1024d,
            _ => amount >= 512 ? amount / 1024d : amount
        };
        if (Math.Abs(gb - 8) < 0.05)
        {
            return "8G";
        }

        if (gb >= 1)
        {
            return gb.ToString("0.#", CultureInfo.InvariantCulture) + "G";
        }

        return Math.Round(gb * 1024).ToString("0", CultureInfo.InvariantCulture) + "M";
    }

    private static string StatusChip(string? status, TimeSpan? age)
    {
        var token = (status ?? "").Trim().ToLowerInvariant();
        if (token is not ("ok" or "stale" or "missing"))
        {
            token = "";
        }

        if (age is TimeSpan span && span > StaleAfter && token != "missing")
        {
            return "STALE";
        }

        return token switch
        {
            "missing" => "MISSING",
            "stale" => "STALE",
            "ok" => "OK",
            _ => age == null ? "MISSING" : "OK"
        };
    }

    private static TimeSpan Drift(DateTime? generatedUtc, DateTime utcNow)
    {
        if (generatedUtc is not DateTime generated)
        {
            return TimeSpan.Zero;
        }

        var drift = utcNow - generated;
        if (drift <= TimeSpan.Zero || drift >= TimeSpan.FromDays(7))
        {
            return TimeSpan.Zero;
        }

        return drift;
    }

    private static string HeapNumbers(double? used, double? max)
    {
        var left = used is double u ? Math.Round(u).ToString("0", CultureInfo.InvariantCulture) : "—";
        var right = max is double m ? Math.Round(m).ToString("0", CultureInfo.InvariantCulture) : "—";
        return left + " / " + right + " MB";
    }

    private static string SizeText(long bytes)
    {
        if (bytes < 0)
        {
            return "";
        }

        double kb = 1024;
        double mb = kb * 1024;
        double gb = mb * 1024;
        if (bytes >= gb)
        {
            return (bytes / gb).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        if (bytes >= mb)
        {
            return (bytes / mb).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= kb)
        {
            return (bytes / kb).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var text = path.Trim().Replace('/', '\\');
        var slash = text.LastIndexOf('\\');
        var name = slash >= 0 ? text[(slash + 1)..] : text;
        return CleanToken(name) ?? "";
    }

    private static string? CleanHost(string? value)
    {
        var text = CleanToken(value);
        if (text == null || text.Contains(' ') || text.Contains('='))
        {
            return null;
        }

        return text;
    }

    private static string? CleanToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length > 80 || ContainsSecret(text))
        {
            return null;
        }

        return text;
    }

    private static bool ContainsSecret(string text) =>
        text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static bool TryObject(string path, out JsonElement root)
    {
        root = default;
        try
        {
            using var doc = JsonDocument.Parse(SharedFile.ReadText(path), JsonOpts);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = doc.RootElement.Clone();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryProp(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? Text(JsonElement obj, string name)
    {
        if (!TryProp(obj, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static double? Number(JsonElement obj, string name)
    {
        if (!TryProp(obj, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? Whole(JsonElement obj, string name)
    {
        var number = Number(obj, name);
        if (number == null)
        {
            return null;
        }

        return (long)Math.Round(number.Value);
    }

    private static bool? Bool(JsonElement obj, string name)
    {
        if (!TryProp(obj, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.String when value.GetString() == "1" => true,
            JsonValueKind.String when value.GetString() == "0" => false,
            _ => null
        };
    }

    private static DateTime? Time(JsonElement obj, string name)
    {
        if (!TryProp(obj, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            if (number > 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)number).UtcDateTime;
            }

            if (number > 1_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)number).UtcDateTime;
            }
        }

        return null;
    }
}
