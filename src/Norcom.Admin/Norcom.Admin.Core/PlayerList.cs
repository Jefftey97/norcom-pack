using System.Text.RegularExpressions;

namespace Norcom.Admin.Core;

public static class PlayerList
{
    public static bool TryParse(string? response, out int online, out int max, out IReadOnlyList<string> names)
    {
        online = 0;
        max = 0;
        names = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var match = Regex.Match(
            response.Trim(),
            @"There are (\d+) of a max of (\d+) players online:?\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return false;
        }

        online = int.Parse(match.Groups[1].Value);
        max = int.Parse(match.Groups[2].Value);
        var tail = match.Groups[3].Value.Trim().TrimEnd('.');
        names = string.IsNullOrWhiteSpace(tail)
            ? Array.Empty<string>()
            : tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return true;
    }
}
