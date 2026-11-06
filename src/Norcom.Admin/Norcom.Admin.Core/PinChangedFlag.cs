namespace Norcom.Admin.Core;

public static class PinChangedFlag
{
    public static string PathFor(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "pin-changed.flag");

    public static bool TryRead(string? serverDirectory, out string pin)
    {
        pin = "";
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return false;
        }

        var path = PathFor(serverDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            pin = SharedFile.ReadText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            pin = "";
        }

        return true;
    }

    public static void Delete(string serverDirectory)
    {
        var path = PathFor(serverDirectory);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
