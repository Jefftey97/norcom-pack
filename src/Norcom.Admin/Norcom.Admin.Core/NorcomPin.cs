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

public sealed class LiveMetaProof
{
    public string Pin { get; init; } = "";

    public string MarbleSha { get; init; } = "";

    public string Mc { get; init; } = "";

    public string Loader { get; init; } = "";
}

public static class NorcomPin
{
    public const string AskServer =
        "Ask NORCOM Server to regenerate the pin before friends sync. Admin did not rewrite server-pin.json.";

    public const string MinLauncher = "2.0.1";

    public const string RepairAction = "resync-pack";

    public const string MarbleJarName = "norcom-marble-1.0.0.jar";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string PinPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "server-pin.json");

    public static string FullLedgerPath(string serverDirectory) =>
        Path.Combine(serverDirectory, "admin", "norcom-mod-pin.json");

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

    public static bool TryReadProof(string? serverDirectory, out LiveMetaProof proof, out string? error)
    {
        proof = new LiveMetaProof();
        error = null;
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            error = "Publish refused. Server folder was not found. Nothing was pushed.";
            return false;
        }

        var path = FullLedgerPath(serverDirectory);
        if (!File.Exists(path))
        {
            error = "Publish refused. Missing live ledger: " + path + ". Nothing was pushed.";
            return false;
        }

        JsonObject ledger;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject obj)
            {
                error = "Publish refused. admin/norcom-mod-pin.json is incomplete. Nothing was pushed.";
                return false;
            }

            ledger = obj;
        }
        catch (JsonException)
        {
            error = "Publish refused. admin/norcom-mod-pin.json could not be read. Nothing was pushed.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Publish refused. admin/norcom-mod-pin.json could not be read. Nothing was pushed.";
            return false;
        }

        if (ledger["jars"] is not JsonArray)
        {
            error = "Publish refused. admin/norcom-mod-pin.json is missing jars. Nothing was pushed.";
            return false;
        }

        var raw = First(ledger, "pin_sha256", "pin");
        if (IsTemporaryPin(raw))
        {
            error = "Publish refused. admin/norcom-mod-pin.json pin is TEMP. Nothing was pushed.";
            return false;
        }

        if (!IsSha256(raw))
        {
            error = "Publish refused. admin/norcom-mod-pin.json pin is incomplete. Nothing was pushed.";
            return false;
        }

        var marble = MarbleSha(ledger);
        if (!IsSha256(marble))
        {
            error = "Publish refused. admin/norcom-mod-pin.json marble sha for " + MarbleJarName + " is missing. Nothing was pushed.";
            return false;
        }

        var mc = First(ledger, "minecraft", "mc");
        var loader = First(ledger, "fabric_loader", "loader");
        if (string.IsNullOrWhiteSpace(mc) || string.IsNullOrWhiteSpace(loader))
        {
            error = "Publish refused. admin/norcom-mod-pin.json is missing mc or loader. Nothing was pushed.";
            return false;
        }

        proof = new LiveMetaProof
        {
            Pin = raw.Trim(),
            MarbleSha = marble!.Trim(),
            Mc = mc.Trim(),
            Loader = loader.Trim()
        };
        return true;
    }

    public static string? StampFriendsMeta(string packDirectory, string serverDirectory)
    {
        if (!TryReadProof(serverDirectory, out var live, out var proofError))
        {
            return proofError;
        }

        var fullPath = FullLedgerPath(serverDirectory);
        var fullText = File.ReadAllText(fullPath);
        var slimPath = PinPath(serverDirectory);
        var slimExisted = File.Exists(slimPath);
        string slimToWrite;
        if (slimExisted)
        {
            string slimBytes;
            ServerPinFile? slim;
            try
            {
                slimBytes = File.ReadAllText(slimPath);
                slim = JsonSerializer.Deserialize<ServerPinFile>(slimBytes, Json);
            }
            catch (JsonException)
            {
                return "Publish refused. Server pin file could not be read. Nothing was pushed.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "Publish refused. Server pin file could not be read. Nothing was pushed.";
            }

            if (slim != null && IsTemporaryPin(slim.pin))
            {
                slim = null;
            }

            if (slim != null)
            {
                if (!SlimOk(slim))
                {
                    return "Publish refused. Server pin is incomplete. Need schema 1 plus mc, loader, and pin. Nothing was pushed.";
                }

                if (!Same(slim.pin, live.Pin))
                {
                    return "Publish refused. pin disagrees between .norcom/server-pin.json (" + slim.pin.Trim() + ") and admin/norcom-mod-pin.json (" + live.Pin + "). Nothing was pushed.";
                }

                if (!SameExact(slim.mc, live.Mc) || !SameExact(slim.loader, live.Loader))
                {
                    return "Publish refused. mc or loader disagrees between .norcom/server-pin.json and admin/norcom-mod-pin.json. Nothing was pushed.";
                }

                slimToWrite = slimBytes;
            }
            else
            {
                slimExisted = false;
                slimToWrite = DerivedSlim(live);
            }
        }
        else
        {
            slimToWrite = DerivedSlim(live);
        }

        var meta = Path.Combine(packDirectory, "meta");
        try
        {
            Directory.CreateDirectory(meta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Publish refused. Could not write meta/. Nothing was pushed.";
        }

        var ledgerError = WriteText("meta/norcom-mod-pin.json", Path.Combine(meta, "norcom-mod-pin.json"), fullText);
        if (ledgerError != null)
        {
            return ledgerError;
        }

        var slimError = WriteText("meta/server-pin.json", Path.Combine(meta, "server-pin.json"), slimToWrite);
        if (slimError != null)
        {
            return slimError;
        }

        var compatError = SyncCompat(Path.Combine(meta, "server-compat.json"), live);
        if (compatError != null)
        {
            return compatError;
        }

        var hintError = SyncHealHint(Path.Combine(meta, "heal-hint.json"), live);
        if (hintError != null)
        {
            return hintError;
        }

        if (!string.Equals(File.ReadAllText(fullPath), fullText, StringComparison.Ordinal))
        {
            return "Publish refused. The live admin pin changed while packing meta. Nothing was pushed.";
        }

        if (slimExisted && !string.Equals(File.ReadAllText(slimPath), slimToWrite, StringComparison.Ordinal))
        {
            return "Publish refused. The live server pin changed while packing meta. Nothing was pushed.";
        }

        return PublishBlock(packDirectory, serverDirectory);
    }

    public static string? PublishBlock(string packDirectory, string serverDirectory)
    {
        if (!TryReadProof(serverDirectory, out var live, out var proofError))
        {
            return proofError;
        }

        foreach (var relative in GitSafety.PackMetaFiles)
        {
            if (!File.Exists(Path.Combine(packDirectory, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                return "Publish refused. Pack is missing " + relative + ". Nothing was pushed.";
            }
        }

        ServerPinFile? packPin;
        JsonObject? compat;
        JsonObject? ledger;
        JsonObject? hint;
        try
        {
            packPin = ReadPin(Path.Combine(packDirectory, "meta", "server-pin.json"));
            compat = ParseObject(Path.Combine(packDirectory, "meta", "server-compat.json"));
            ledger = ParseObject(Path.Combine(packDirectory, "meta", "norcom-mod-pin.json"));
            hint = ParseObject(Path.Combine(packDirectory, "meta", "heal-hint.json"));
        }
        catch (JsonException)
        {
            return "Publish refused. Pack pin meta could not be read. Nothing was pushed.";
        }

        if (!SlimOk(packPin))
        {
            return "Publish refused. Pack meta/server-pin.json is missing schema, mc, loader, or pin. Nothing was pushed.";
        }

        var slim = FieldDiffs(new ServerPinFile { mc = live.Mc, loader = live.Loader, pin = live.Pin }, packPin!, "meta/server-pin.json");
        if (slim.Count > 0)
        {
            return "Publish refused. " + string.Join(" ", slim) + " Nothing was pushed.";
        }

        if (compat == null || string.IsNullOrWhiteSpace(Text(compat, "pin")) || IsTemporaryPin(Text(compat, "pin")))
        {
            return "Publish refused. meta/server-compat.json is missing pin. Nothing was pushed.";
        }

        if (!Same(live.Pin, Text(compat, "pin")))
        {
            return "Publish refused. pin disagrees in meta/server-compat.json (server " + live.Pin + ", pack " + Text(compat, "pin").Trim() + "). Nothing was pushed.";
        }

        if (!SameExact(live.Mc, Text(compat, "mc")))
        {
            return "Publish refused. mc disagrees in meta/server-compat.json (server " + live.Mc + ", pack " + Text(compat, "mc").Trim() + "). Nothing was pushed.";
        }

        if (!SameExact(live.Loader, Text(compat, "loader")))
        {
            return "Publish refused. loader disagrees in meta/server-compat.json (server " + live.Loader + ", pack " + Text(compat, "loader").Trim() + "). Nothing was pushed.";
        }

        if (ledger == null || ledger["jars"] is not JsonArray)
        {
            return "Publish refused. meta/norcom-mod-pin.json is missing jars. Nothing was pushed.";
        }

        var ledgerPin = First(ledger, "pin_sha256", "pin");
        if (!Same(live.Pin, ledgerPin))
        {
            return "Publish refused. pin disagrees in meta/norcom-mod-pin.json (server " + live.Pin + ", pack " + ledgerPin.Trim() + "). Nothing was pushed.";
        }

        var marble = MarbleSha(ledger);
        if (!Same(live.MarbleSha, marble ?? ""))
        {
            return "Publish refused. marble sha disagrees in meta/norcom-mod-pin.json (server " + live.MarbleSha + ", pack " + (marble ?? "") + "). Nothing was pushed.";
        }

        if (hint == null ||
            !Same(live.Pin, Text(hint, "pin")) ||
            !SameExact(live.Mc, Text(hint, "mc")) ||
            !SameExact(live.Loader, Text(hint, "loader")) ||
            !SameExact(MinLauncher, Text(hint, "minLauncher")) ||
            !SameExact(RepairAction, Text(hint, "repairAction")))
        {
            return "Publish refused. meta/heal-hint.json is not synced to the live pin. Nothing was pushed.";
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

    public static string? MarbleSha(JsonObject ledger)
    {
        if (ledger["jars"] is not JsonArray jars)
        {
            return null;
        }

        foreach (var jar in jars)
        {
            if (jar is not JsonObject obj)
            {
                continue;
            }

            if (!string.Equals(Text(obj, "name"), MarbleJarName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sha = Text(obj, "sha256");
            return string.IsNullOrWhiteSpace(sha) ? null : sha.Trim();
        }

        return null;
    }

    public static bool IsTemporaryPin(string? pin)
    {
        var text = (pin ?? "").Trim();
        return text.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("TEMP-", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("TEMP_", StringComparison.OrdinalIgnoreCase);
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

    private static string DerivedSlim(LiveMetaProof live) =>
        JsonSerializer.Serialize(new ServerPinFile
        {
            schema = 1,
            mc = live.Mc,
            loader = live.Loader,
            pin = live.Pin,
            source = "admin",
            updatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, Json);

    private static string? SyncCompat(string path, LiveMetaProof live)
    {
        JsonObject obj;
        if (File.Exists(path))
        {
            try
            {
                if (ParseObject(path) is not JsonObject parsed)
                {
                    return "Publish refused. meta/server-compat.json could not be read. Nothing was pushed.";
                }

                obj = parsed;
            }
            catch (JsonException)
            {
                return "Publish refused. meta/server-compat.json could not be read. Nothing was pushed.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "Publish refused. meta/server-compat.json could not be read. Nothing was pushed.";
            }
        }
        else
        {
            obj = new JsonObject();
        }

        if (Same(Text(obj, "pin"), live.Pin) &&
            SameExact(Text(obj, "mc"), live.Mc) &&
            SameExact(Text(obj, "loader"), live.Loader) &&
            !IsTemporaryPin(Text(obj, "pin")))
        {
            return null;
        }

        obj["pin"] = live.Pin;
        obj["mc"] = live.Mc;
        obj["loader"] = live.Loader;
        return WriteText("meta/server-compat.json", path, obj.ToJsonString(Json));
    }

    private static string? SyncHealHint(string path, LiveMetaProof live)
    {
        JsonObject obj;
        if (File.Exists(path))
        {
            try
            {
                if (ParseObject(path) is not JsonObject parsed)
                {
                    return "Publish refused. meta/heal-hint.json could not be read. Nothing was pushed.";
                }

                obj = parsed;
            }
            catch (JsonException)
            {
                return "Publish refused. meta/heal-hint.json could not be read. Nothing was pushed.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "Publish refused. meta/heal-hint.json could not be read. Nothing was pushed.";
            }
        }
        else
        {
            obj = new JsonObject();
        }

        if (Same(Text(obj, "pin"), live.Pin) &&
            SameExact(Text(obj, "mc"), live.Mc) &&
            SameExact(Text(obj, "loader"), live.Loader) &&
            SameExact(Text(obj, "minLauncher"), MinLauncher) &&
            SameExact(Text(obj, "repairAction"), RepairAction))
        {
            return null;
        }

        obj["pin"] = live.Pin;
        obj["mc"] = live.Mc;
        obj["loader"] = live.Loader;
        obj["minLauncher"] = MinLauncher;
        obj["repairAction"] = RepairAction;
        return WriteText("meta/heal-hint.json", path, obj.ToJsonString(Json));
    }

    private static string? WriteText(string relative, string path, string contents)
    {
        try
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), contents, StringComparison.Ordinal))
            {
                return null;
            }

            File.WriteAllText(path, contents);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Publish refused. Could not write " + relative + ". Nothing was pushed.";
        }
    }

    private static JsonObject? ParseObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject;

    private static ServerPinFile? ReadPin(string path) =>
        JsonSerializer.Deserialize<ServerPinFile>(File.ReadAllText(path), Json);

    private static bool SlimOk(ServerPinFile? file) =>
        file != null && file.schema == 1 &&
        !string.IsNullOrWhiteSpace(file.mc) &&
        !string.IsNullOrWhiteSpace(file.loader) &&
        IsSha256(file.pin);

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

    private static string Text(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var node) || node is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return "";
        }

        return text ?? "";
    }

    private static string First(JsonObject obj, string primary, string alias)
    {
        var text = Text(obj, primary);
        return string.IsNullOrWhiteSpace(text) ? Text(obj, alias) : text;
    }

    private static bool IsSha256(string? pin)
    {
        var text = (pin ?? "").Trim();
        if (text.Length != 64)
        {
            return false;
        }

        foreach (var c in text)
        {
            var hex = c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameExact(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}
