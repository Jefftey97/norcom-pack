using System.Text.Json;
using System.Text.Json.Nodes;

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

public sealed class HealHintFile
{
    public string pin { get; set; } = "";

    public string mc { get; set; } = "";

    public string loader { get; set; } = "";

    public string minLauncher { get; set; } = "2.0.1";

    public string repairAction { get; set; } = "resync-pack";
}

public static class NorcomPin
{
    public const string AskServer =
        "Ask NORCOM Server to regenerate the pin before friends sync. Admin did not rewrite server-pin.json.";

    public const string MinLauncher = "2.0.1";

    public const string RepairAction = "resync-pack";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string PinPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "server-pin.json");

    public static string PingPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "admin-autofix.ping");

    public static ServerPinFile? TryRead(string? serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return null;
        }

        var path = PinPath(serverDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ReadPin(path);
        }
        catch
        {
            return null;
        }
    }

    public static string? StampFriendsMeta(string packDirectory, string serverDirectory)
    {
        var sotPath = PinPath(serverDirectory);
        if (!File.Exists(sotPath))
        {
            return "Publish refused. Missing server pin: " + sotPath + ". Nothing was pushed.";
        }

        var sotBytes = File.ReadAllText(sotPath);
        ServerPinFile? sot;
        try
        {
            sot = JsonSerializer.Deserialize<ServerPinFile>(sotBytes, Json);
        }
        catch (JsonException)
        {
            return "Publish refused. Server pin file could not be read. Nothing was pushed.";
        }

        if (!SlimOk(sot))
        {
            return "Publish refused. Server pin is incomplete. Need schema 1 plus mc, loader, and pin. Nothing was pushed.";
        }

        var ledgerSource = FindLedger(packDirectory, serverDirectory);
        if (ledgerSource == null)
        {
            return "Publish refused. Missing meta file: meta/norcom-mod-pin.json. Nothing was pushed.";
        }

        JsonObject ledger;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(ledgerSource)) is not JsonObject obj)
            {
                return "Publish refused. meta/norcom-mod-pin.json is incomplete. Nothing was pushed.";
            }

            ledger = obj;
        }
        catch (JsonException)
        {
            return "Publish refused. meta/norcom-mod-pin.json could not be read. Nothing was pushed.";
        }

        var meta = Path.Combine(packDirectory, "meta");
        Directory.CreateDirectory(meta);
        var compat = ReadCompat(Path.Combine(meta, "server-compat.json"));
        compat.pin = sot!.pin;
        compat.mc = sot.mc;
        compat.loader = sot.loader;
        ledger["pin"] = sot.pin;
        ledger["mc"] = sot.mc;
        ledger["loader"] = sot.loader;

        File.WriteAllText(Path.Combine(meta, "server-pin.json"), sotBytes);
        File.WriteAllText(Path.Combine(meta, "server-compat.json"), JsonSerializer.Serialize(compat, Json));
        File.WriteAllText(Path.Combine(meta, "norcom-mod-pin.json"), ledger.ToJsonString(Json));
        WriteHealHintIfChanged(Path.Combine(meta, "heal-hint.json"), sot);

        if (!string.Equals(File.ReadAllText(sotPath), sotBytes, StringComparison.Ordinal))
        {
            return "Publish refused. The live server pin changed while packing meta. Nothing was pushed.";
        }

        return PublishBlock(packDirectory, serverDirectory);
    }

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

    private static string? FindLedger(string packDirectory, string serverDirectory)
    {
        var packLedger = Path.Combine(packDirectory, "meta", "norcom-mod-pin.json");
        if (File.Exists(packLedger))
        {
            return packLedger;
        }

        var serverLedger = Path.Combine(serverDirectory, "NORCOM-pack", "meta", "norcom-mod-pin.json");
        return File.Exists(serverLedger) ? serverLedger : null;
    }

    private static ServerCompatFile ReadCompat(string path)
    {
        if (!File.Exists(path))
        {
            return new ServerCompatFile();
        }

        try
        {
            return JsonSerializer.Deserialize<ServerCompatFile>(File.ReadAllText(path), Json) ?? new ServerCompatFile();
        }
        catch (JsonException)
        {
            return new ServerCompatFile();
        }
    }

    private static void WriteHealHintIfChanged(string path, ServerPinFile sot)
    {
        if (File.Exists(path))
        {
            try
            {
                var current = JsonSerializer.Deserialize<HealHintFile>(File.ReadAllText(path), Json);
                if (current != null &&
                    string.Equals(current.pin?.Trim(), sot.pin.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(current.minLauncher?.Trim(), MinLauncher, StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (JsonException)
            {
            }
        }

        File.WriteAllText(path, JsonSerializer.Serialize(new HealHintFile
        {
            pin = sot.pin,
            mc = sot.mc,
            loader = sot.loader,
            minLauncher = MinLauncher,
            repairAction = RepairAction
        }, Json));
    }

    public static string Prefix(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0)
        {
            return "—";
        }

        return text.Length <= 8 ? text : text[..8] + "…";
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
