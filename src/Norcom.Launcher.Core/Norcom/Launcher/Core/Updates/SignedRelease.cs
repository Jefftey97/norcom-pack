using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Norcom.Launcher.Core.Updates;

public sealed record ReleasePayload(int Schema, string Product, string Channel, string KeyId,
    long Sequence, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, string Version,
    int Protocol, string PackageUrl, long PackageBytes, string PackageSha256,
    long ExecutableBytes, string ExecutableSha256);

public sealed class VerifiedRelease
{
    public ReleasePayload Payload { get; }
    public string Envelope { get; }
    public string PayloadDigest { get; }
    internal VerifiedRelease(ReleasePayload payload, string envelope, string digest)
        => (Payload, Envelope, PayloadDigest) = (payload, envelope, digest);
}

public sealed class ReleaseVerifier
{
    public const int MaximumEnvelopeBytes = 32768;
    public const long MaximumPackageBytes = 268435456;
    public const string Product = "norcom-friends-launcher";
    public const string Channel = "friends";
    public static readonly byte[] Domain = System.Text.Encoding.ASCII.GetBytes("NORCOM signed launcher release v1\n");
    private readonly IReadOnlyDictionary<string, string> _publicKeys;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public ReleaseVerifier(IReadOnlyDictionary<string, string> publicKeys)
        => _publicKeys = new Dictionary<string, string>(publicKeys, StringComparer.Ordinal);
    public VerifiedRelease Verify(string envelope, DateTimeOffset now, bool installing = true)
    {
        try
        {
            if (envelope == null || Encoding.UTF8.GetByteCount(envelope) > MaximumEnvelopeBytes) Reject();
            using var outer = JsonDocument.Parse(envelope);
            ExactProperties(outer.RootElement, "format", "keyId", "payload", "signature");
            var root = outer.RootElement;
            var keyId = root.GetProperty("keyId").GetString();
            if (root.GetProperty("format").GetString() != "norcom-release-v1" || keyId == null || !_publicKeys.ContainsKey(keyId)) Reject();
            byte[] payloadBytes = Convert.FromBase64String(root.GetProperty("payload").GetString() ?? "");
            byte[] signature = Convert.FromBase64String(root.GetProperty("signature").GetString() ?? "");
            if (payloadBytes.Length == 0 || payloadBytes.Length > 16384 || signature.Length != 64) Reject();
            using var key = ECDsa.Create();
            key.ImportFromPem(_publicKeys[keyId]);
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                !key.VerifyData(Domain.Concat(payloadBytes).ToArray(), signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) Reject();
            using var doc = JsonDocument.Parse(payloadBytes);
            ExactProperties(doc.RootElement, "schema", "product", "channel", "keyId", "sequence", "issuedUtc", "expiresUtc", "version",
                "protocol", "packageUrl", "packageBytes", "packageSha256", "executableBytes", "executableSha256");
            var p = JsonSerializer.Deserialize<ReleasePayload>(payloadBytes, Options);
            if (p == null || p.Schema != 1 || p.Product != Product || p.Channel != Channel || p.KeyId != keyId ||
                p.Sequence <= 0 || p.Protocol != 1 || !CanonicalVersion(p.Version) ||
                p.IssuedUtc.Offset != TimeSpan.Zero || p.ExpiresUtc.Offset != TimeSpan.Zero ||
                p.ExpiresUtc <= p.IssuedUtc || p.ExpiresUtc - p.IssuedUtc > TimeSpan.FromDays(30) ||
                (installing && (p.IssuedUtc > now.AddMinutes(5) || p.ExpiresUtc <= now)) ||
                !IsHash(p.PackageSha256) || !IsHash(p.ExecutableSha256) ||
                p.PackageBytes < 1 || p.PackageBytes > MaximumPackageBytes ||
                p.ExecutableBytes < 1 || p.ExecutableBytes > MaximumPackageBytes || !AllowedPackageUri(p.PackageUrl)) Reject();
            return new VerifiedRelease(p, envelope, Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant());
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException ||
                                   ex is CryptographicException || ex is ArgumentException || ex is KeyNotFoundException)
        { throw new InvalidDataException("The launcher release signature or policy is invalid.", ex); }
    }

    public static bool AllowedPackageUri(string value)
    {
        // Immutable repository objects only. No user info, redirect targets, fragments,
        // queries, escaping, Unicode host aliases or arbitrary signed download origins.
        return value != null && Regex.IsMatch(value,
            @"\Ahttps://raw\.githubusercontent\.com/Jefftey97/norcom-pack/[a-f0-9]{40}/launcher/[A-Za-z0-9][A-Za-z0-9._-]{0,100}\.zip\z",
            RegexOptions.CultureInvariant);
    }
    public static bool IsHash(string value) => value != null && Regex.IsMatch(value, @"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    public static bool CanonicalVersion(string value) => value != null && Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z", RegexOptions.CultureInvariant);
    internal static void ExactProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) Reject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) Reject();
        if (seen.Count != names.Length) Reject();
    }
    private static void Reject() => throw new InvalidDataException("The launcher release signature or policy is invalid.");
}
