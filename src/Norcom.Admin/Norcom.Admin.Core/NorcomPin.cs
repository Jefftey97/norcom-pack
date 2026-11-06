using System.Text.Json;

namespace Norcom.Admin.Core;

public sealed class ServerPinFile
{
    public int schema { get; set; } = 1;

    public string mc { get; set; } = "";

    public string loader { get; set; } = "";

    public string pin { get; set; } = "";

    public string source { get; set; } = "";

    public string updatedUtc { get; set; } = "";
}

public sealed class ServerCompatFile
{
    public string? packVersion { get; set; }

    public string? indexSha256 { get; set; }

    public string? both { get; set; }

    public string? pin { get; set; }

    public string? mc { get; set; }

    public string? loader { get; set; }
}

public static class NorcomPin
{
    public const string AskServer =
        "Ask NORCOM Server to regenerate the pin before friends sync. Admin did not rewrite server-pin.json.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string PinPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "server-pin.json");

    public static string PingPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "admin-autofix.ping");

    public static string? PublishBlock(string packDirectory, string serverDirectory)
    {
        var sotPath = PinPath(serverDirectory);
        if (!File.Exists(sotPath))
        {
            return "Publish refused. Server pin file is missing at " + sotPath + ". Nothing was pushed.";
        }

        ServerPinFile? sot;
        try
        {
            sot = ReadPin(sotPath);
        }
        catch (JsonException)
        {
            return "Publish refused. Server pin file could not be read. Nothing was pushed.";
        }

        if (!SlimOk(sot))
        {
            return "Publish refused. Server pin file is missing schema, mc, loader, or pin. Nothing was pushed.";
        }

        foreach (var relative in GitSafety.PackMetaFiles)
        {
            if (!File.Exists(Path.Combine(packDirectory, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                return "Publish refused. Pack is missing " + relative + ". Nothing was pushed.";
            }
        }

        ServerPinFile? packPin;
        ServerCompatFile? compat;
        try
        {
            packPin = ReadPin(Path.Combine(packDirectory, "meta", "server-pin.json"));
            compat = JsonSerializer.Deserialize<ServerCompatFile>(File.ReadAllText(Path.Combine(packDirectory, "meta", "server-compat.json")), Json);
        }
        catch (JsonException)
        {
            return "Publish refused. Pack pin meta could not be read. Nothing was pushed.";
        }

        if (!SlimOk(packPin))
        {
            return "Publish refused. Pack meta/server-pin.json is missing schema, mc, loader, or pin. Nothing was pushed.";
        }

        var slim = FieldDiffs(sot!, packPin!, "meta/server-pin.json");
        if (slim.Count > 0)
        {
            return "Publish refused. " + string.Join(" ", slim) + " Nothing was pushed.";
        }

        if (compat == null || string.IsNullOrWhiteSpace(compat.pin))
        {
            return "Publish refused. meta/server-compat.json is missing pin. Nothing was pushed.";
        }

        if (!Same(sot!.pin, compat.pin))
        {
            return "Publish refused. pin disagrees in meta/server-compat.json (server " + sot.pin.Trim() + ", pack " + compat.pin.Trim() + "). Nothing was pushed.";
        }

        if (!string.IsNullOrWhiteSpace(compat.mc) && !SameExact(sot.mc, compat.mc))
        {
            return "Publish refused. mc disagrees in meta/server-compat.json (server " + sot.mc.Trim() + ", pack " + compat.mc.Trim() + "). Nothing was pushed.";
        }

        if (!string.IsNullOrWhiteSpace(compat.loader) && !SameExact(sot.loader, compat.loader))
        {
            return "Publish refused. loader disagrees in meta/server-compat.json (server " + sot.loader.Trim() + ", pack " + compat.loader.Trim() + "). Nothing was pushed.";
        }

        return null;
    }

    public static void NoteJarMove(string serverDirectory, string jarName, DateTime utc)
    {
        var when = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
        var stamp = when.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var directory = Path.GetDirectoryName(PingPath(serverDirectory))!;
        Directory.CreateDirectory(directory);
        var line = stamp + " jar=" + jarName + " " + AskServer;
        File.AppendAllText(PingPath(serverDirectory), line + Environment.NewLine);
    }

    private static ServerPinFile? ReadPin(string path) =>
        JsonSerializer.Deserialize<ServerPinFile>(File.ReadAllText(path), Json);

    private static bool SlimOk(ServerPinFile? file) =>
        file != null && file.schema == 1 &&
        !string.IsNullOrWhiteSpace(file.mc) &&
        !string.IsNullOrWhiteSpace(file.loader) &&
        !string.IsNullOrWhiteSpace(file.pin);

    private static List<string> FieldDiffs(ServerPinFile sot, ServerPinFile pack, string file)
    {
        var diffs = new List<string>();
        if (!SameExact(sot.mc, pack.mc))
        {
            diffs.Add("mc disagrees in " + file + " (server " + sot.mc.Trim() + ", pack " + pack.mc.Trim() + ").");
        }

        if (!SameExact(sot.loader, pack.loader))
        {
            diffs.Add("loader disagrees in " + file + " (server " + sot.loader.Trim() + ", pack " + pack.loader.Trim() + ").");
        }

        if (!Same(sot.pin, pack.pin))
        {
            diffs.Add("pin disagrees in " + file + " (server " + sot.pin.Trim() + ", pack " + pack.pin.Trim() + ").");
        }

        return diffs;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameExact(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}
