using System.Security.Cryptography;
using System.Text;
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

public static class NorcomPin
{
    public const string AutoFixSource = "Admin-AutoFix";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static string PinPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "server-pin.json");

    public static string PingPath(string serverDirectory) =>
        Path.Combine(serverDirectory, ".norcom", "server-pin.ping");

    public static string ComputePackPin(string packDirectory)
    {
        var index = Path.Combine(packDirectory, "index.toml");
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(index))).ToLowerInvariant();
    }

    public static ServerPinFile? TryRead(string serverDirectory)
    {
        var path = PinPath(serverDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ServerPinFile>(File.ReadAllText(path), Json);
    }

    public static string? PublishBlock(string packDirectory, string serverDirectory)
    {
        var path = PinPath(serverDirectory);
        if (!File.Exists(path))
        {
            return "Publish refused. Server pin file is missing at " + path + ". Nothing was pushed.";
        }

        ServerPinFile? file;
        try
        {
            file = TryRead(serverDirectory);
        }
        catch (JsonException)
        {
            return "Publish refused. Server pin file could not be read. Nothing was pushed.";
        }

        if (file == null || file.schema != 1 || string.IsNullOrWhiteSpace(file.pin))
        {
            return "Publish refused. Server pin file is missing a schema 1 pin. Nothing was pushed.";
        }

        var packPin = ComputePackPin(packDirectory);
        if (!string.Equals(packPin, file.pin.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Publish refused. Pack index pin " + packPin + " does not match server pin " + file.pin.Trim() + ". Nothing was pushed.";
        }

        return null;
    }

    public static ServerPinFile BumpForJarMove(string serverDirectory, string jarName, DateTime utc)
    {
        var previous = TryRead(serverDirectory);
        var when = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
        var stamp = when.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var next = new ServerPinFile
        {
            schema = 1,
            mc = previous?.mc ?? "",
            loader = previous?.loader ?? "",
            pin = BumpPin(previous?.pin, jarName, stamp),
            source = AutoFixSource,
            updatedUtc = stamp
        };
        var directory = Path.GetDirectoryName(PinPath(serverDirectory))!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(PinPath(serverDirectory), JsonSerializer.Serialize(next, Json));
        var line = stamp + " source=" + AutoFixSource + " jar=" + jarName + " pin=" + next.pin;
        File.AppendAllText(PingPath(serverDirectory), line + Environment.NewLine);
        return next;
    }

    public static string BumpPin(string? previousPin, string jarName, string updatedUtc)
    {
        var payload = Encoding.UTF8.GetBytes((previousPin ?? "") + "\n" + jarName + "\n" + updatedUtc + "\n" + AutoFixSource);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
