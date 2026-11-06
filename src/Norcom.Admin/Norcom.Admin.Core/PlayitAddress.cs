using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public static class PlayitAddress
{
    private static readonly Regex Host = new(
        @"([a-zA-Z0-9][a-zA-Z0-9.-]*\.(?:ply\.gg|playit\.gg))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Describe(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return "No Playit file";
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.Contains("playit", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var line in SharedFile.ReadLines(file).Take(80))
            {
                if (line.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = Host.Match(line);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return "Playit file, no hostname";
        }

        return "No Playit file";
    }

    public static bool IsDown(string described) =>
        described.StartsWith("No Playit", StringComparison.Ordinal) ||
        described.Contains("no hostname", StringComparison.OrdinalIgnoreCase);
}
