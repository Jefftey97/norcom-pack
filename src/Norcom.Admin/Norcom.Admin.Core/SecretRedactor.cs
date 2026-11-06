using System.Text;
using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public sealed class SecretRedactor
{
    private static readonly Regex PasswordAssign = new(
        @"(?i)\b(rcon\.password|password|rcon-password|token|pat)\s*[:=]\s*\S+",
        RegexOptions.Compiled);

    private static readonly Regex TokenShape = new(
        @"(?i)\b(?:ghp_[A-Za-z0-9]+|github_pat_[A-Za-z0-9_]+|x-access-token[:\s]+\S+|bearer\s+\S+)\b",
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
        cleaned = TokenShape.Replace(cleaned, "<redacted>");
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
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:o} {safe}{Environment.NewLine}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
        }
    }
}
