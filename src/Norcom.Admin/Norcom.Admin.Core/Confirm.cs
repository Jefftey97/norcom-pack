namespace Norcom.Admin.Core;

public static class Confirm
{
    public static bool Matches(string? typed, string word) =>
        string.Equals((typed ?? "").Trim(), word, StringComparison.Ordinal);
}
