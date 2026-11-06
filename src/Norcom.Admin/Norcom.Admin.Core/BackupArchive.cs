using System.IO.Compression;

namespace Norcom.Admin.Core;

public sealed record BackupEntry(string Name, string FullPath, DateTime WrittenUtc, bool IsZip);

public static class BackupArchive
{
    public static string CreateZip(string serverDirectory)
    {
        if (ServerCommands.JavaAppearsUp(serverDirectory))
        {
            throw new InvalidOperationException(BackupPolicy.LiveWorldMessage);
        }

        var world = ServerPresence.WorldDirectory(serverDirectory);
        if (!Directory.Exists(world))
        {
            throw new DirectoryNotFoundException("No world folder to back up.");
        }

        var backups = Path.Combine(serverDirectory, "backups");
        Directory.CreateDirectory(backups);
        var zipPath = Path.Combine(backups, "world-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(world, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        }

        return zipPath;
    }

    public static IReadOnlyList<BackupEntry> List(string serverDirectory)
    {
        var backups = Path.Combine(serverDirectory, "backups");
        if (!Directory.Exists(backups))
        {
            return Array.Empty<BackupEntry>();
        }

        var files = Directory.EnumerateFiles(backups, "*.zip", SearchOption.AllDirectories)
            .Select(path => new BackupEntry(Path.GetRelativePath(backups, path), path, File.GetLastWriteTimeUtc(path), true));
        var folders = Directory.EnumerateDirectories(backups)
            .Where(path => !Path.GetFileName(path).Equals("auto", StringComparison.OrdinalIgnoreCase))
            .Select(path => new BackupEntry(Path.GetFileName(path), path, Directory.GetLastWriteTimeUtc(path), false));
        return files.Concat(folders).OrderByDescending(entry => entry.WrittenUtc).Take(40).ToList();
    }

    public static string Age(string serverDirectory)
    {
        var latest = List(serverDirectory).FirstOrDefault();
        if (latest == null)
        {
            return "No backup yet";
        }

        var age = DateTime.UtcNow - latest.WrittenUtc;
        return (int)age.TotalHours + "h " + age.Minutes + "m ago";
    }

    public static void Restore(string serverDirectory, string backupPath)
    {
        if (ServerCommands.JavaAppearsUp(serverDirectory))
        {
            throw new InvalidOperationException("Java is still up. Restore waits until the world is quiet.");
        }

        var full = Path.GetFullPath(backupPath);
        var backups = Path.GetFullPath(Path.Combine(serverDirectory, "backups"));
        if (!full.StartsWith(backups + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Restore only reads files inside the backups folder.");
        }

        var world = ServerPresence.WorldDirectory(serverDirectory);
        var hold = Path.Combine(serverDirectory, "world-before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var moved = false;
        if (Directory.Exists(world))
        {
            Directory.Move(world, hold);
            moved = true;
        }

        try
        {
            if (full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(full, world);
            }
            else if (Directory.Exists(full))
            {
                CopyTree(full, world);
            }
            else
            {
                throw new FileNotFoundException("That backup was not found.", full);
            }
        }
        catch
        {
            if (Directory.Exists(world))
            {
                Directory.Delete(world, recursive: true);
            }

            if (moved)
            {
                Directory.Move(hold, world);
            }

            throw;
        }
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (!Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyTree(directory, Path.Combine(dest, Path.GetFileName(directory)));
        }
    }
}
