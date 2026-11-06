using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public sealed class SecretRedactor
{
    private static readonly Regex PasswordAssign = new(
        @"(?i)\b(rcon\.password|password|rcon-password)\s*=\s*\S+",
        RegexOptions.Compiled);

    private string _secret = "";

    public void Remember(string? secret) => _secret = secret ?? "";

    public string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var cleaned = text;
        if (_secret.Length > 0)
        {
            cleaned = cleaned.Replace(_secret, "<redacted>", StringComparison.Ordinal);
        }

        cleaned = PasswordAssign.Replace(cleaned, "$1=<redacted>");
        if (cleaned.Length > 4000)
        {
            cleaned = cleaned[..4000] + "…";
        }

        return cleaned;
    }

    public void WriteLog(string line)
    {
        try
        {
            var logPath = AdminPaths.LogPath;
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var safe = Clean(line).Replace('\r', ' ').Replace('\n', ' ');
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:o} {safe}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
