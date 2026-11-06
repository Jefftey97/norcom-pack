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
        File.Exists(Path.Combine(serverDirectory, "world", "session.lock"));

    public static bool JavaAppearsUp(string serverDirectory) =>
        ProcessAlive(serverDirectory) || SessionLockPresent(serverDirectory);

    public static string WritingReason(string serverDirectory)
    {
        var process = ProcessAlive(serverDirectory);
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
            return "session.lock";
        }

        return "quiet";
    }

    public static IReadOnlyList<string> ListConfigs(string serverDirectory)
    {
        var config = Path.Combine(serverDirectory, "config");
        if (!Directory.Exists(config))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(config, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".toml" or ".json" or ".json5" or ".properties" or ".txt" or ".cfg")
            .Select(path => Path.GetRelativePath(config, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToList();
    }

    public static string ReadConfig(string serverDirectory, string relative) =>
        File.ReadAllText(ResolveConfig(serverDirectory, relative));

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
        var root = Path.GetFullPath(Path.Combine(serverDirectory, "config"));
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That config path is outside the server config folder.");
        }

        return full;
    }
}
