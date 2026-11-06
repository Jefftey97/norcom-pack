namespace Norcom.Admin.Core;

public static class IndexGuard
{
    public static bool IsInstallerVersion(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        return relativePath.Replace('\\', '/').Trim().Trim('"').Trim('\'').EndsWith("installer-version.txt", StringComparison.OrdinalIgnoreCase);
    }

    public static string StripInstallerVersion(string indexToml)
    {
        var newline = indexToml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = indexToml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>();
        var block = new List<string>();
        var inFiles = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
            {
                if (inFiles)
                {
                    Flush();
                }

                inFiles = trimmed.Equals("[[files]]", StringComparison.OrdinalIgnoreCase);
                if (inFiles)
                {
                    block.Add(line);
                    continue;
                }
            }

            if (inFiles)
            {
                block.Add(line);
            }
            else
            {
                output.Add(line);
            }
        }

        if (inFiles)
        {
            Flush();
        }

        var text = string.Join('\n', output).TrimEnd() + "\n";
        return newline == "\r\n" ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;

        void Flush()
        {
            if (!BlockListsInstaller(block))
            {
                output.AddRange(block);
            }

            block.Clear();
        }
    }

    private static bool BlockListsInstaller(List<string> block)
    {
        foreach (var line in block)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var split = trimmed.IndexOf('=');
            if (split >= 0 && IsInstallerVersion(trimmed[(split + 1)..].Trim().Trim('"').Trim('\'')))
            {
                return true;
            }
        }

        return false;
    }
}
