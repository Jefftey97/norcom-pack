using System.Text.Json;

namespace Norcom.Admin.Core;

public static class HealFails
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null, WriteIndented = false };

    public static string PathFor(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "heal-fails.json");

    public static int CountFor(string? serverDirectory, string? pin)
    {
        var note = Read(serverDirectory);
        if (note == null || string.IsNullOrWhiteSpace(pin))
        {
            return 0;
        }

        return string.Equals(note.pin?.Trim(), pin.Trim(), StringComparison.OrdinalIgnoreCase) ? Math.Max(0, note.count) : 0;
    }

    public static bool BlocksMetaPublish(string? serverDirectory, string? pin) =>
        CountFor(serverDirectory, pin) >= 2;

    public static void NoteClientFail(string serverDirectory, string pin)
    {
        var note = Read(serverDirectory);
        var count = note != null && string.Equals(note.pin?.Trim(), pin.Trim(), StringComparison.OrdinalIgnoreCase)
            ? note.count + 1
            : 1;
        var directory = Path.GetDirectoryName(PathFor(serverDirectory))!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(serverDirectory), JsonSerializer.Serialize(new Note { pin = pin.Trim(), count = count }, Json));
    }

    public static void ConsumeClientMarker(string serverDirectory, string fallbackPin)
    {
        var marker = Path.Combine(serverDirectory, ".norcom", "client-heal-fail");
        if (!File.Exists(marker))
        {
            return;
        }

        var pin = fallbackPin;
        try
        {
            var text = SharedFile.ReadText(marker).Trim();
            if (text.Length > 0)
            {
                pin = text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            File.Delete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (!string.IsNullOrWhiteSpace(pin))
        {
            NoteClientFail(serverDirectory, pin);
        }
    }

    private static Note? Read(string? serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return null;
        }

        var path = PathFor(serverDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Note>(File.ReadAllText(path), Json);
        }
        catch
        {
            return null;
        }
    }

    private sealed class Note
    {
        public string pin { get; set; } = "";

        public int count { get; set; }
    }
}
