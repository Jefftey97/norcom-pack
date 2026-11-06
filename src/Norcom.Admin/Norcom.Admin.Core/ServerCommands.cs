using System.Diagnostics;

namespace Norcom.Admin.Core;

public static class ServerCommands
{
    public static StartPlan PlanStart(string serverDirectory, int memoryMb)
    {
        _ = memoryMb;
        if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
        {
            return new StartPlan(StartKind.Missing, "", Array.Empty<string>(), "Server folder was not found.");
        }

        foreach (var name in new[] { "start.bat", "run.bat", "start.ps1" })
        {
            var path = Path.Combine(serverDirectory, name);
            if (File.Exists(path))
            {
                return new StartPlan(StartKind.Script, path, Array.Empty<string>(), "Starting " + name);
            }
        }

        var fabric = Path.Combine(serverDirectory, "fabric-server-launch.jar");
        var server = Path.Combine(serverDirectory, "server.jar");
        var jar = File.Exists(fabric) ? fabric : File.Exists(server) ? server : "";
        if (jar.Length == 0)
        {
            return new StartPlan(StartKind.Missing, "", Array.Empty<string>(), "No start script or server jar was found.");
        }

        return new StartPlan(
            StartKind.JavaJar,
            jar,
            ["-Xms8G", "-Xmx8G", "-jar", jar, "nogui"],
            "Starting " + Path.GetFileName(jar) + " with Xms and Xmx at 8G.");
    }

    public static bool ProcessAlive(string serverDirectory)
    {
        var pidPath = Path.Combine(serverDirectory, "norcom-admin.pid");
        if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath).Trim(), out var pid))
        {
            return false;
        }

        try
        {
            return !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static bool SessionLockPresent(string serverDirectory) =>
        ServerPresence.SessionLockExists(serverDirectory);

    public static bool JavaAppearsUp(string serverDirectory) =>
        ServerPresence.JavaServes(serverDirectory) || SessionLockPresent(serverDirectory);

    public static string WritingReason(string serverDirectory)
    {
        var process = ServerPresence.JavaServes(serverDirectory);
        var session = SessionLockPresent(serverDirectory);
        if (process && session)
        {
            return "Java process and session.lock";
        }

        if (process)
        {
            return "Java process";
        }

        if (session)
        {
            return "session.lock in " + ServerPresence.LevelName(serverDirectory);
        }

        return "quiet";
    }

    public static IReadOnlyList<string> ListConfigs(string serverDirectory)
    {
        var roots = new[]
        {
            "config",
            "defaultconfigs",
            Path.Combine(ServerPresence.LevelName(serverDirectory), "serverconfig")
        };
        var files = new List<string>();
        foreach (var relativeRoot in roots)
        {
            var root = Path.Combine(serverDirectory, relativeRoot);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(path) is ".toml" or ".json" or ".json5" or ".properties" or ".txt" or ".cfg" or ".conf")
                {
                    files.Add(Path.GetRelativePath(serverDirectory, path));
                }
            }
        }

        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(400).ToList();
    }

    public static string ReadConfig(string serverDirectory, string relative) =>
        SharedFile.ReadText(ResolveConfig(serverDirectory, relative));

    public static void SaveConfig(string serverDirectory, string relative, string content)
    {
        var path = ResolveConfig(serverDirectory, relative);
        var backup = Path.Combine(serverDirectory, "config", ".norcom-backup", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (File.Exists(path))
        {
            File.Copy(path, backup, overwrite: true);
        }

        File.WriteAllText(path, content);
    }

    public static string ResolveConfig(string serverDirectory, string relative)
    {
        var server = Path.GetFullPath(serverDirectory);
        var full = Path.GetFullPath(Path.Combine(server, relative));
        var allowed = new[]
        {
            Path.GetFullPath(Path.Combine(server, "config")),
            Path.GetFullPath(Path.Combine(server, "defaultconfigs")),
            Path.GetFullPath(Path.Combine(ServerPresence.WorldDirectory(serverDirectory), "serverconfig"))
        };
        var inside = allowed.Any(root =>
            full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            full.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (!inside)
        {
            throw new InvalidOperationException("That config path is outside the server config folders.");
        }

        return full;
    }
}
