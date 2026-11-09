using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class SignedReleaseTests : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-11-09T12:00:00Z");
    private ReleaseVerifier Verifier => new(new Dictionary<string,string> { ["test-only"] = key.ExportSubjectPublicKeyInfoPem() });
    private ReleasePayload Payload => new(1, ReleaseVerifier.Product, ReleaseVerifier.Channel, "test-only", 1,
        now.AddMinutes(-1), now.AddDays(7), "2.0.46", 1,
        "https://raw.githubusercontent.com/Jefftey97/norcom-pack/" + new string('a',40) + "/launcher/NorcomWpf-2.0.46.zip",
        100, new string('a',64), 200, new string('b',64));
    internal static string Sign(ECDsa key, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var signature = key.SignData(ReleaseVerifier.Domain.Concat(bytes).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.Serialize(new { format = "norcom-release-v1", keyId = "test-only",
            payload = Convert.ToBase64String(bytes), signature = Convert.ToBase64String(signature) });
    }
    private string Sign(ReleasePayload payload) => Sign(key, JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    [Fact] public void AcceptsExactSignedPayload()
    {
        var envelope = Sign(Payload);
        var verified = Verifier.Verify(envelope, now);
        Assert.Equal(Payload, verified.Payload);
        Assert.Equal(envelope, verified.Envelope);
        Assert.Equal(64, verified.PayloadDigest.Length);
    }
    [Fact] public void RejectsPayloadAndSignatureTampering()
    {
        var envelope = Sign(Payload);
        using var doc = JsonDocument.Parse(envelope);
        var bad = JsonSerializer.Serialize(new { format="norcom-release-v1", keyId="test-only",
            payload=Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")), signature=doc.RootElement.GetProperty("signature").GetString() });
        Assert.Throws<InvalidDataException>(() => Verifier.Verify(bad,now));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Throws<InvalidDataException>(() => Verifier.Verify(Sign(other, JsonSerializer.Serialize(Payload)),now));
    }
    [Theory]
    [InlineData("product")][InlineData("channel")][InlineData("sequence")][InlineData("future")]
    [InlineData("expired")][InlineData("period")][InlineData("version")][InlineData("protocol")]
    [InlineData("hash")][InlineData("size")][InlineData("schema")][InlineData("key")]
    public void RejectsAuthenticatedButInvalidPolicy(string flaw)
    {
        var p = flaw switch {
            "product" => Payload with {Product="owner"}, "channel" => Payload with {Channel="admin"},
            "sequence" => Payload with {Sequence=0}, "future" => Payload with {IssuedUtc=now.AddHours(1)},
            "expired" => Payload with {ExpiresUtc=now.AddMinutes(-2)}, "period" => Payload with {ExpiresUtc=now.AddDays(31)},
            "version" => Payload with {Version="../../evil"}, "protocol" => Payload with {Protocol=2},
            "hash" => Payload with {ExecutableSha256=new string('z',64)}, "size" => Payload with {PackageBytes=long.MaxValue},
            "schema" => Payload with {Schema=2}, _ => Payload with {KeyId="other"} };
        Assert.Throws<InvalidDataException>(() => Verifier.Verify(Sign(p),now));
    }
    [Theory]
    [InlineData("http://raw.githubusercontent.com/Jefftey97/norcom-pack/main/launcher/x.zip")]
    [InlineData("https://localhost/x.zip")]
    [InlineData("https://raw.githubusercontent.com.evil.test/Jefftey97/norcom-pack/main/launcher/x.zip")]
    [InlineData("https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/launcher/x.zip")]
    [InlineData("https://user@raw.githubusercontent.com/Jefftey97/norcom-pack/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/launcher/x.zip")]
    [InlineData("https://raw.githubusercontent.com:444/Jefftey97/norcom-pack/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/launcher/x.zip")]
    [InlineData("https://raw.githubusercontent.com/Jefftey97/norcom-pack/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/launcher/x.zip?q=1")]
    [InlineData("https://raw.githubusercontent.com/Jefftey97/norcom-pack/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/launcher/%2e%2e/x.zip")]
    public void RejectsUnapprovedPackageSource(string url)
        => Assert.Throws<InvalidDataException>(() => Verifier.Verify(Sign(Payload with {PackageUrl=url}),now));

    [Fact] public void RejectsUnknownAndDuplicatePropertiesEvenWhenSigned()
    {
        var p = JsonSerializer.Serialize(Payload,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        foreach (var suffix in new[] {",\"sequence\":1}", ",\"extra\":true}"})
            Assert.Throws<InvalidDataException>(() => Verifier.Verify(Sign(key,p[..^1]+suffix),now));
        var envelope = Sign(Payload);
        Assert.Throws<InvalidDataException>(() => Verifier.Verify(envelope[..^1]+",\"format\":\"norcom-release-v1\"}",now));
        Assert.Throws<InvalidDataException>(() => Verifier.Verify(new string(' ',40000),now));
    }
    [Fact] public void PreviouslyVerifiedPayloadCanBeCheckedForRecoveryAfterExpiry()
    {
        var envelope=Sign(Payload);
        Assert.Throws<InvalidDataException>(()=>Verifier.Verify(envelope, now.AddDays(10)));
        Assert.Equal(Payload,Verifier.Verify(envelope,now.AddDays(10),installing:false).Payload);
    }
    public void Dispose() => key.Dispose();
}
