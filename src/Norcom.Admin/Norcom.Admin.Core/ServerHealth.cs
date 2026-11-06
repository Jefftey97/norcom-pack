namespace Norcom.Admin.Core;

public static class ServerHealth
{
    public static string Score(bool msptHigh, bool diskHigh, bool swapCritical, bool tunnelDown)
    {
        var flags = new List<string>();
        if (msptHigh)
        {
            flags.Add("MSPT above 40");
        }

        if (diskHigh)
        {
            flags.Add("disk high");
        }

        if (swapCritical)
        {
            flags.Add("swap critical");
        }

        if (tunnelDown)
        {
            flags.Add("tunnel down");
        }

        return flags.Count == 0 ? "Clear" : string.Join(", ", flags);
    }
}
