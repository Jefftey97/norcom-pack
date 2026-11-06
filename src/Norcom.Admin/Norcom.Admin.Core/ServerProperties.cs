namespace Norcom.Admin.Core;

public static class ServerProperties
{
    public static readonly string[] LockedKeys = ["simulation-distance", "rcon.password", "rcon-password"];

    public static Dictionary<string, string> Read(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
        {
            return map;
        }

        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var split = line.IndexOf('=');
            if (split > 0)
            {
                map[line[..split].Trim()] = line[(split + 1)..].Trim();
            }
        }

        return map;
    }

    public static Dictionary<string, string> PublicView(IReadOnlyDictionary<string, string> all)
    {
        var copy = new Dictionary<string, string>(all, StringComparer.OrdinalIgnoreCase);
        copy.Remove("rcon.password");
        copy.Remove("rcon-password");
        return copy;
    }

    public static bool IsLocked(string key) =>
        LockedKeys.Contains(key, StringComparer.OrdinalIgnoreCase) ||
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    public static void WriteSecret(string path, string password)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var wrotePassword = false;
        var wroteEnable = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("rcon.password=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "rcon.password=" + password;
                wrotePassword = true;
            }
            else if (trimmed.StartsWith("enable-rcon=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "enable-rcon=true";
                wroteEnable = true;
            }
        }

        if (!wrotePassword)
        {
            lines.Add("rcon.password=" + password);
        }

        if (!wroteEnable)
        {
            lines.Add("enable-rcon=true");
        }

        File.WriteAllText(path, string.Join(newline, lines).TrimEnd() + newline);
    }

    public static string Upsert(string? existing, IReadOnlyDictionary<string, string> updates)
    {
        if (updates.Keys.Any(IsLocked))
        {
            throw new InvalidOperationException("Password keys and simulation-distance are not written from this editor.");
        }

        var newline = existing != null && existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = string.IsNullOrEmpty(existing)
            ? new List<string>()
            : existing.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var split = trimmed.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = trimmed[..split].Trim();
            if (updates.TryGetValue(key, out var value))
            {
                lines[i] = key + "=" + value;
                seen.Add(key);
            }
        }

        foreach (var update in updates)
        {
            if (!seen.Contains(update.Key))
            {
                lines.Add(update.Key + "=" + update.Value);
            }
        }

        return string.Join(newline, lines) + newline;
    }
}
