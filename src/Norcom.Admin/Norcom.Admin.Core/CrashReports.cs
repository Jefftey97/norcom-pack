namespace Norcom.Admin.Core;

public static class CrashReports
{
    public static (IReadOnlyList<string> Files, string LastFault) Read(string serverDirectory)
    {
        try
        {
            var folder = Path.Combine(serverDirectory, "crash-reports");
            if (!Directory.Exists(folder))
            {
                return (Array.Empty<string>(), "No crash reports.");
            }

            var files = Directory.GetFiles(folder).OrderByDescending(File.GetLastWriteTimeUtc).Take(20).ToList();
            var names = files.Select(path => Path.GetFileName(path) ?? "").Where(name => name.Length > 0).ToList();
            if (files.Count == 0)
            {
                return (names, "No crash reports.");
            }

            var body = SharedFile.ReadText(files[0]);
            return (names, names[0] + ": " + FaultLine(body));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (Array.Empty<string>(), "Crash reports are in use.");
        }
    }

    public static string FaultLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Exception", StringComparison.Ordinal) ||
                line.StartsWith("java.", StringComparison.Ordinal))
            {
                return line.Length > 240 ? line[..240] : line;
            }
        }

        var first = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0);
        return string.IsNullOrEmpty(first) ? "No fault line in the newest crash report." : (first.Length > 240 ? first[..240] : first);
    }
}
