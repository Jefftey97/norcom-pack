using System.Text;
using System.Text.Json;

namespace Norcom.Admin.Core;

public interface IRemoteMetaCheck
{
    Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken);

    Task<RemoteBody> ReadAsync(string url, CancellationToken cancellationToken);
}

public sealed record RemoteBody(int Status, string Body);

public sealed record MetaAudit(bool Ok, string Report);

public sealed class HttpRemoteMetaCheck : IRemoteMetaCheck
{
    public async Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(url, cancellationToken).ConfigureAwait(false);
        return read.Status;
    }

    public async Task<RemoteBody> ReadAsync(string url, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "NORCOM-Admin");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new RemoteBody((int)response.StatusCode, body);
    }
}

public static class MetaRemote
{
    public static readonly string[] Files =
    [
        "meta/server-pin.json",
        "meta/server-compat.json",
        "meta/norcom-mod-pin.json",
        "meta/heal-hint.json"
    ];

    public static string ContentsUrl(string relative) =>
        "https://api.github.com/repos/Jefftey97/norcom-pack/contents/" + relative + "?ref=main";

    public static string? RawCommitUrl(string? sha, string relative)
    {
        var commit = (sha ?? "").Trim();
        if (!IsCommitSha(commit))
        {
            return null;
        }

        return "https://raw.githubusercontent.com/Jefftey97/norcom-pack/" + commit + "/" + relative;
    }

    public static Task<MetaAudit> AuditContentsAsync(LiveMetaProof live, IRemoteMetaCheck remote, CancellationToken cancellationToken) =>
        AuditAsync(live, remote, commitSha: null, allowRawCommit: false, cancellationToken);

    public static Task<MetaAudit> AuditPushedAsync(string commitSha, LiveMetaProof live, IRemoteMetaCheck remote, CancellationToken cancellationToken) =>
        AuditAsync(live, remote, commitSha, allowRawCommit: true, cancellationToken);

    public static bool TryDecodeContents(string body, out string text)
    {
        text = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (root.TryGetProperty("encoding", out var encoding) &&
                encoding.ValueKind == JsonValueKind.String &&
                !string.Equals(encoding.GetString(), "base64", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var raw = content.GetString() ?? "";
            var bytes = Convert.FromBase64String(string.Concat(raw.Where(c => !char.IsWhiteSpace(c))));
            text = Encoding.UTF8.GetString(bytes);
            return text.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return false;
        }
    }

    private static async Task<MetaAudit> AuditAsync(
        LiveMetaProof live,
        IRemoteMetaCheck remote,
        string? commitSha,
        bool allowRawCommit,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var usedRaw = false;
        foreach (var relative in Files)
        {
            var contents = await FetchContentsAsync(remote, relative, cancellationToken).ConfigureAwait(false);
            if (contents != null && Drift(relative, contents, live) == null)
            {
                continue;
            }

            var rawUrl = allowRawCommit ? RawCommitUrl(commitSha, relative) : null;
            if (rawUrl != null)
            {
                var raw = await FetchRawAsync(remote, rawUrl, cancellationToken).ConfigureAwait(false);
                if (raw != null && Drift(relative, raw, live) == null)
                {
                    usedRaw = true;
                    continue;
                }
            }

            if (contents == null && rawUrl == null)
            {
                failures.Add(relative + " missing from contents API");
                continue;
            }

            if (contents == null)
            {
                failures.Add(relative + " missing from contents API and raw@" + (commitSha ?? "").Trim());
                continue;
            }

            failures.Add(relative + " " + Drift(relative, contents, live));
        }

        if (failures.Count > 0)
        {
            return new MetaAudit(false, string.Join("; ", failures));
        }

        var via = usedRaw ? "raw@" + (commitSha ?? "").Trim() : "contents API";
        var report = via + " pin " + NorcomPin.Prefix(live.Pin) + " marble " + NorcomPin.Prefix(live.MarbleSha) + " matches LIVE";
        return new MetaAudit(true, report);
    }

    private static async Task<string?> FetchContentsAsync(IRemoteMetaCheck remote, string relative, CancellationToken cancellationToken)
    {
        var read = await ReadBodyAsync(remote, ContentsUrl(relative), cancellationToken).ConfigureAwait(false);
        if (read == null || read.Status != 200 || !TryDecodeContents(read.Body, out var text))
        {
            return null;
        }

        return text;
    }

    private static async Task<string?> FetchRawAsync(IRemoteMetaCheck remote, string url, CancellationToken cancellationToken)
    {
        if (url.Contains("/norcom-pack/main/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var read = await ReadBodyAsync(remote, url, cancellationToken).ConfigureAwait(false);
        if (read == null || read.Status != 200 || string.IsNullOrWhiteSpace(read.Body))
        {
            return null;
        }

        return read.Body;
    }

    private static async Task<RemoteBody?> ReadBodyAsync(IRemoteMetaCheck remote, string url, CancellationToken cancellationToken)
    {
        try
        {
            return await remote.ReadAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Drift(string relative, string json, LiveMetaProof live)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (relative.EndsWith("norcom-mod-pin.json", StringComparison.Ordinal))
            {
                return DriftLedger(root, live);
            }

            if (relative.EndsWith("heal-hint.json", StringComparison.Ordinal))
            {
                return DriftHint(root, live);
            }

            return DriftPin(root, live, relative);
        }
        catch (JsonException)
        {
            return "could not be read";
        }
    }

    private static string? DriftLedger(JsonElement root, LiveMetaProof live)
    {
        var pin = Field(root, "pin_sha256", "pin");
        if (NorcomPin.IsTemporaryPin(pin) || !Same(pin, live.Pin))
        {
            return "pin_sha256 drift (GitHub " + Show(pin) + ", live " + live.Pin + ")";
        }

        var marble = Marble(root);
        if (!Same(marble, live.MarbleSha))
        {
            return "marble sha drift (GitHub " + Show(marble) + ", live " + live.MarbleSha + ")";
        }

        return null;
    }

    private static string? DriftHint(JsonElement root, LiveMetaProof live)
    {
        var pin = DriftPin(root, live, "meta/heal-hint.json");
        if (pin != null)
        {
            return pin;
        }

        if (!SameExact(Field(root, "minLauncher"), NorcomPin.MinLauncher))
        {
            return "minLauncher drift (GitHub " + Show(Field(root, "minLauncher")) + ", live " + NorcomPin.MinLauncher + ")";
        }

        if (!SameExact(Field(root, "repairAction"), NorcomPin.RepairAction))
        {
            return "repairAction drift (GitHub " + Show(Field(root, "repairAction")) + ", live " + NorcomPin.RepairAction + ")";
        }

        return null;
    }

    private static string? DriftPin(JsonElement root, LiveMetaProof live, string relative)
    {
        var pin = Field(root, "pin", "pin_sha256");
        if (NorcomPin.IsTemporaryPin(pin) || !Same(pin, live.Pin))
        {
            return "pin drift (GitHub " + Show(pin) + ", live " + live.Pin + ")";
        }

        if (!SameExact(Field(root, "mc", "minecraft"), live.Mc))
        {
            return "mc drift in " + relative + " (GitHub " + Show(Field(root, "mc", "minecraft")) + ", live " + live.Mc + ")";
        }

        if (!SameExact(Field(root, "loader", "fabric_loader"), live.Loader))
        {
            return "loader drift in " + relative + " (GitHub " + Show(Field(root, "loader", "fabric_loader")) + ", live " + live.Loader + ")";
        }

        return null;
    }

    private static string Marble(JsonElement root)
    {
        if (!root.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var jar in jars.EnumerateArray())
        {
            if (!SameExact(Field(jar, "name"), NorcomPin.MarbleJarName))
            {
                continue;
            }

            return Field(jar, "sha256");
        }

        return "";
    }

    private static string Field(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = (value.GetString() ?? "").Trim();
            if (text.Length > 0)
            {
                return text;
            }
        }

        return "";
    }

    private static string Show(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 0 ? "missing" : text;
    }

    private static bool IsCommitSha(string sha) =>
        sha.Length >= 7 &&
        sha.Length <= 64 &&
        !sha.Equals("main", StringComparison.OrdinalIgnoreCase) &&
        sha.All(c => c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F');

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameExact(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}
