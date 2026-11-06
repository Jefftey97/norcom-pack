using System.Text.Json;

namespace Norcom.Admin.Core;

public static class ServerOverviewReader
{
    public static ServerOverview Read(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new ServerOverview(
                false, "—", "—", "—", "—", "—", "—", "—",
                Array.Empty<string>(), Array.Empty<string>(), 0, 0,
                "No Playit file", "No latest.log yet.", "No config folder.");
        }

        try
        {
            return ReadUnlocked(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ServerOverview(
                true, "—", "—", "—", "—", "—", "—", "—",
                Array.Empty<string>(), Array.Empty<string>(), 0, 0,
                "No Playit file", "latest.log is in use.", "Server files are busy.");
        }
    }

    private static ServerOverview ReadUnlocked(string directory)
    {
        var props = ReadProperties(Path.Combine(directory, "server.properties"));
        return new ServerOverview(
            true,
            Value("online-mode"),
            Value("motd"),
            Value("server-port"),
            Value("max-players"),
            Value("white-list"),
            Value("view-distance"),
            Value("simulation-distance"),
            ReadNames(Path.Combine(directory, "whitelist.json")),
            ReadNames(Path.Combine(directory, "ops.json")),
            CountFiles(Path.Combine(directory, "crash-reports")),
            CountFiles(Path.Combine(directory, "backups")),
            PlayitAddress.Describe(directory),
            ReadTick(Path.Combine(directory, "logs", "latest.log")),
            ReadFeatures(directory));

        string Value(string key) => props.TryGetValue(key, out var value) && value.Length > 0 ? value : "—";
    }

    public static Dictionary<string, string> ReadProperties(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return map;
        }

        foreach (var raw in SharedFile.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map[key] = line[(split + 1)..].Trim();
        }

        return map;
    }

    public static IReadOnlyList<string> ReadNames(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(SharedFile.ReadText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    names.Add(item.GetString() ?? "");
                }
                else if (item.TryGetProperty("name", out var name))
                {
                    names.Add(name.GetString() ?? "");
                }
            }

            return names.Where(name => name.Length > 0).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static string DiskFree(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return "Unknown";
            }

            var gigabytes = new DriveInfo(root).AvailableFreeSpace / 1073741824d;
            return gigabytes < 5 ? "low " + gigabytes.ToString("0.0") + " GB" : gigabytes.ToString("0.0") + " GB";
        }
        catch
        {
            return "Unknown";
        }
    }

    public static bool DiskLow(string diskText) => diskText.Contains("low", StringComparison.OrdinalIgnoreCase);

    private static int CountFiles(string path) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() : 0;

    private static string ReadFeatures(string directory)
    {
        var config = Path.Combine(directory, "config");
        if (!Directory.Exists(config))
        {
            return "No config folder, so economy and quest settings are not listed.";
        }

        var names = Directory.EnumerateDirectories(config)
            .Select(Path.GetFileName)
            .Where(name => name != null && (
                name.Contains("econom", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("quest", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("spark", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("job", StringComparison.OrdinalIgnoreCase)))
            .Cast<string>()
            .ToArray();
        return names.Length == 0
            ? "No economy, quest, job, or spark config folder was found."
            : "Related config folders: " + string.Join(", ", names) + ".";
    }

    private static string ReadTick(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return "No latest.log yet.";
            }

            string? last = null;
            foreach (var line in SharedFile.ReadLines(logPath).TakeLast(400))
            {
                if (line.Contains("TPS", StringComparison.OrdinalIgnoreCase) || line.Contains("MSPT", StringComparison.OrdinalIgnoreCase))
                {
                    last = line.Trim();
                }
            }

            return last ?? "latest.log has no TPS or MSPT line yet.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "latest.log is in use.";
        }
    }
}
