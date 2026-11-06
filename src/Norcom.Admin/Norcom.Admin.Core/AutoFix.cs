namespace Norcom.Admin.Core;

public static class AutoFix
{
    public static bool ShouldRestartFrozen(bool enabled, bool processAlive, DateTime? lastLogUtc, DateTime now, TimeSpan quietFor) =>
        enabled && processAlive && lastLogUtc.HasValue && now - lastLogUtc.Value >= quietFor;

    public static IReadOnlyList<string> CleanupCandidates(string serverDirectory, DateTime now, int keepDays)
    {
        var list = new List<string>();
        foreach (var folder in new[] { "logs", "crash-reports" })
        {
            var path = Path.Combine(serverDirectory, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                if (Path.GetFileName(file).Equals("latest.log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (now - File.GetLastWriteTimeUtc(file) >= TimeSpan.FromDays(Math.Max(1, keepDays)))
                {
                    list.Add(file);
                }
            }
        }

        return list;
    }

    public static void DeleteOldLogs(string serverDirectory, DateTime now, int keepDays)
    {
        foreach (var file in CleanupCandidates(serverDirectory, now, keepDays))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }
    }

    public static bool LooksBrokenJson(string text)
    {
        var body = text.Trim();
        if (body.Length == 0)
        {
            return true;
        }

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return false;
        }

        var stack = new Stack<char>();
        var inString = false;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < body.Length)
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c is '{' or '[')
            {
                stack.Push(c);
            }
            else if (c is '}' or ']')
            {
                if (stack.Count == 0)
                {
                    return true;
                }

                var open = stack.Pop();
                if ((c == '}' && open != '{') || (c == ']' && open != '['))
                {
                    return true;
                }
            }
        }

        return inString || stack.Count > 0;
    }

    public static bool TryRestoreBrokenConfig(string configPath, string backupPath)
    {
        if (!File.Exists(configPath) || !File.Exists(backupPath))
        {
            return false;
        }

        if (!configPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            !configPath.EndsWith(".json5", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!LooksBrokenJson(File.ReadAllText(configPath)) || LooksBrokenJson(File.ReadAllText(backupPath)))
        {
            return false;
        }

        File.Copy(backupPath, configPath, overwrite: true);
        return true;
    }

    public static string? ModNamedInCrash(string crashText)
    {
        foreach (var line in crashText.Split('\n'))
        {
            var jar = line.IndexOf(".jar", StringComparison.OrdinalIgnoreCase);
            if (jar < 0)
            {
                continue;
            }

            var start = Math.Max(line.LastIndexOf(' ', jar), Math.Max(line.LastIndexOf('/', jar), line.LastIndexOf('\\', jar))) + 1;
            var name = line[start..(jar + 4)].Trim();
            if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !name.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    public static string? RollbackBadMod(string serverDirectory, string crashText)
    {
        var name = ModNamedInCrash(crashText);
        if (name == null)
        {
            return null;
        }

        var mod = Path.Combine(serverDirectory, "mods", name);
        if (!File.Exists(mod))
        {
            return null;
        }

        var quarantine = Path.Combine(serverDirectory, "mods-quarantine");
        Directory.CreateDirectory(quarantine);
        File.Copy(mod, Path.Combine(quarantine, name), overwrite: true);
        File.Delete(mod);
        var previous = Path.Combine(serverDirectory, "mods-backup", name);
        if (File.Exists(previous))
        {
            File.Copy(previous, mod, overwrite: true);
        }

        NorcomPin.NoteJarMove(serverDirectory, name, DateTime.UtcNow);
        return name;
    }

    public static IReadOnlyList<string> NewCrashReports(string serverDirectory, ISet<string> alreadySeen)
    {
        var path = Path.Combine(serverDirectory, "crash-reports");
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        var fresh = new List<string>();
        foreach (var file in Directory.EnumerateFiles(path))
        {
            var name = Path.GetFileName(file);
            if (alreadySeen.Add(name))
            {
                fresh.Add(name);
            }
        }

        return fresh;
    }
}
