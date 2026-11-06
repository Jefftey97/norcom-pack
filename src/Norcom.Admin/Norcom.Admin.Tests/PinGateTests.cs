using System.Text;
using System.Text.Json;
using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class PinGateTests
{
    private const string Pin = "07983921b11ec8e8443bb307817c7b10c90c4388491c45d05da2a819108936ac";

    private const string HeadPin = "49ff8e68f03f40d4eacdb98ad9848d17f977ef1bf2cc1f08a1e8b216c70bb9d1";

    private const string Marble = "47fe426d3c4edca916b605a6196bd32831f5b00c01c3e61d6e34410e5e82230d";

    private const string Commit = "0e94a8e6ceac5c52b683b3e51c48e4f2a170f641";

    [Fact]
    public async Task Publish_refuses_when_the_live_ledger_is_missing()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.Delete(NorcomPin.FullLedgerPath(server));
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.Pushed);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("norcom-mod-pin.json", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was pushed", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Publish_stamps_friends_meta_from_the_server_pin()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var sot = File.ReadAllText(NorcomPin.PinPath(server));
        WriteSlim(Path.Combine(pack, "meta", "server-pin.json"), "not-the-pin", "9.9", "0.0.1");
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.Contains("git status failed", result.Message, StringComparison.Ordinal);
        Assert.Equal(sot, File.ReadAllText(NorcomPin.PinPath(server)));
        Assert.Equal(sot, File.ReadAllText(Path.Combine(pack, "meta", "server-pin.json")));
        Assert.Equal(File.ReadAllText(NorcomPin.FullLedgerPath(server)), File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")));
        Assert.Contains(Marble, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        var hint = JsonSerializer.Deserialize<HealHintFile>(File.ReadAllText(Path.Combine(pack, "meta", "heal-hint.json")));
        Assert.Equal(Pin, hint!.pin);
        Assert.Equal("26.3", hint.mc);
        Assert.Equal("0.20.1", hint.loader);
        Assert.Equal(NorcomPin.MinLauncher, hint.minLauncher);
        Assert.Equal(NorcomPin.RepairAction, hint.repairAction);
        Assert.Contains(Pin, File.ReadAllText(Path.Combine(pack, "meta", "server-compat.json")), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("push", string.Join('\n', git.Calls), StringComparison.OrdinalIgnoreCase);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Publish_copies_the_live_ledger_when_the_pack_copy_is_missing()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.Delete(Path.Combine(pack, "meta", "norcom-mod-pin.json"));
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.Contains("git status failed", result.Message, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(NorcomPin.FullLedgerPath(server)), File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")));
        Assert.Contains(Marble, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        Assert.NotEmpty(git.Calls);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Publish_refuses_when_server_compat_pin_disagrees()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.WriteAllText(Path.Combine(pack, "meta", "server-compat.json"), "{\"packVersion\":\"1.0.35\",\"indexSha256\":\"abc\",\"both\":\"both\",\"pin\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"mc\":\"26.3\",\"loader\":\"0.20.1\"}");
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.Contains("git status failed", result.Message, StringComparison.Ordinal);
        Assert.Contains(Pin, File.ReadAllText(Path.Combine(pack, "meta", "server-compat.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.0.35", File.ReadAllText(Path.Combine(pack, "meta", "server-compat.json")), StringComparison.Ordinal);
        Assert.NotEmpty(git.Calls);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Publish_replaces_a_stale_pack_ledger_with_the_live_admin_pin()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.WriteAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json"), "{\"pin\":\"ledger-is-not-the-gate\",\"jars\":[\"stale.jar\"]}");
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.Contains("git status failed", result.Message, StringComparison.Ordinal);
        var ledger = File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json"));
        Assert.Contains(Pin, ledger, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Marble, ledger, StringComparison.Ordinal);
        Assert.Contains("kept.jar", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("stale.jar", ledger, StringComparison.Ordinal);
        Assert.NotEmpty(git.Calls);
        Assert.DoesNotContain("push", string.Join('\n', git.Calls), StringComparison.OrdinalIgnoreCase);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Meta_publish_is_already_live_only_when_contents_api_matches_pin_and_marble()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new RecordingGit();
        var remote = ContentsRemote(Pin, Marble, Pin);
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, remote, CancellationToken.None);
        Assert.True(result.Ok);
        Assert.True(result.ClearFlag);
        Assert.False(result.Pushed);
        Assert.Contains("ALREADY_LIVE pin " + NorcomPin.Prefix(Pin), result.Message, StringComparison.Ordinal);
        Assert.Contains("marble " + NorcomPin.Prefix(Marble), result.Message, StringComparison.Ordinal);
        Assert.Contains("contents API", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
        Assert.Equal(4, remote.Urls.Count);
        Assert.All(remote.Urls, url =>
        {
            Assert.Contains("api.github.com", url, StringComparison.Ordinal);
            Assert.Contains("ref=main", url, StringComparison.Ordinal);
        });
        Assert.Contains(remote.Urls, url => url.Contains("norcom-mod-pin.json", StringComparison.Ordinal));
        Assert.Contains(remote.Urls, url => url.Contains("server-pin.json", StringComparison.Ordinal));
        Assert.Contains(remote.Urls, url => url.Contains("server-compat.json", StringComparison.Ordinal));
        Assert.Contains(remote.Urls, url => url.Contains("heal-hint.json", StringComparison.Ordinal));
        Assert.DoesNotContain(remote.Urls, url => url.Contains("/norcom-pack/main/", StringComparison.Ordinal));
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Meta_publish_does_not_treat_http_200_as_already_live()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new RecordingGit();
        var remote = new BodyRemote((_, _) => (200, ""));
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, remote, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.ClearFlag);
        Assert.DoesNotContain("ALREADY_LIVE", result.Message, StringComparison.Ordinal);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Meta_publish_pushes_four_files_and_fails_red_when_marble_drifts()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new PushGit();
        var drifted = new string('a', 64);
        var remote = ContentsRemote(Pin, drifted, Pin);
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, remote, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.ClearFlag);
        Assert.True(result.Pushed);
        Assert.Contains("marble sha drift", result.Message, StringComparison.Ordinal);
        Assert.Contains("Not ALREADY_LIVE", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ALREADY_LIVE pin", result.Message, StringComparison.Ordinal);
        var add = string.Join('\n', git.Calls);
        Assert.Contains("push --ff-only origin main", add, StringComparison.Ordinal);
        foreach (var file in MetaRemote.Files)
        {
            Assert.Contains(file, add, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(remote.Urls, url => url.Contains("/norcom-pack/main/", StringComparison.Ordinal));
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Verify_does_not_trust_raw_main_cdn()
    {
        var server = ProofServer();
        var remote = new BodyRemote((url, _) =>
            url.Contains("/norcom-pack/main/", StringComparison.Ordinal)
                ? (200, LedgerJson(Pin, Marble))
                : (404, ""));
        Assert.True(NorcomPin.TryReadProof(server, out var proof, out _));
        var audit = await MetaRemote.AuditPushedAsync("main", proof, remote, CancellationToken.None);
        Assert.False(audit.Ok);
        Assert.Null(MetaRemote.RawCommitUrl("main", "meta/norcom-mod-pin.json"));
        Assert.DoesNotContain(remote.Urls, url => url.Contains("/norcom-pack/main/", StringComparison.Ordinal));
        Assert.DoesNotContain(remote.Urls, url => url.Contains("raw.githubusercontent.com", StringComparison.Ordinal));
        Directory.Delete(server, true);
    }

    [Fact]
    public async Task Verify_accepts_raw_commit_when_contents_api_is_missing()
    {
        var server = ProofServer();
        Assert.True(NorcomPin.TryReadProof(server, out var proof, out _));
        var remote = new BodyRemote((url, _) =>
        {
            if (url.Contains("/norcom-pack/main/", StringComparison.Ordinal))
            {
                return (200, LedgerJson(new string('b', 64), Marble));
            }

            if (url.Contains(Commit, StringComparison.Ordinal))
            {
                return (200, FileJson(url, proof.Pin, proof.MarbleSha));
            }

            return (404, "");
        });
        var audit = await MetaRemote.AuditPushedAsync(Commit, proof, remote, CancellationToken.None);
        Assert.True(audit.Ok);
        Assert.Contains("raw@" + Commit, audit.Report, StringComparison.Ordinal);
        Assert.DoesNotContain(remote.Urls, url => url.Contains("/norcom-pack/main/", StringComparison.Ordinal));
        Directory.Delete(server, true);
    }

    [Fact]
    public async Task Meta_publish_does_not_skip_a_temp_compat_pin()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new PushGit();
        var remote = new BodyRemote((url, _) =>
        {
            if (url.Contains("/norcom-pack/main/", StringComparison.Ordinal))
            {
                return (200, FileJson(url, Pin, Marble));
            }

            if (!url.Contains("api.github.com", StringComparison.Ordinal) && !url.Contains(Commit, StringComparison.Ordinal))
            {
                return (404, "");
            }

            var json = url.Contains("server-compat.json", StringComparison.Ordinal)
                ? "{\"pin\":\"TEMP\",\"mc\":\"26.3\",\"loader\":\"0.20.1\"}"
                : FileJson(url, Pin, Marble);
            return (200, url.Contains("api.github.com", StringComparison.Ordinal) ? Wrap(json) : json);
        });
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, remote, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.ClearFlag);
        Assert.True(result.Pushed);
        Assert.Contains("TEMP", result.Message, StringComparison.Ordinal);
        Assert.Contains("Not ALREADY_LIVE", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ALREADY_LIVE pin", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(remote.Urls, url => url.Contains("/norcom-pack/main/", StringComparison.Ordinal));
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Packwiz_publish_fails_red_when_marble_drifts_after_push()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new PushGit();
        var remote = ContentsRemote(Pin, new string('c', 64), Pin);
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, remote, CancellationToken.None);
        Assert.True(result.Pushed);
        Assert.False(result.Ok);
        Assert.False(result.ClearFlag);
        Assert.Contains("marble sha drift", result.Message, StringComparison.Ordinal);
        Assert.Contains("Not ALREADY_LIVE", result.Message, StringComparison.Ordinal);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Meta_publish_stops_after_two_heal_fails()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        HealFails.NoteClientFail(server, Pin);
        HealFails.NoteClientFail(server, Pin);
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, new OkRemote(), CancellationToken.None);
        Assert.False(result.Pushed);
        Assert.Contains("PinGate", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
        Cleanup(pack, server);
    }

    [Fact]
    public void Heal_hint_is_left_alone_when_live_fields_match()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var hint = Path.Combine(pack, "meta", "heal-hint.json");
        File.WriteAllText(hint, "{\"source\":\"keep\",\"pin\":\"" + Pin + "\",\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"minLauncher\":\"2.0.1\",\"repairAction\":\"resync-pack\"}");
        var before = File.ReadAllText(hint);
        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.Null(block);
        Assert.Equal(before, File.ReadAllText(hint));
        Cleanup(pack, server);
    }

    [Fact]
    public void Publish_maps_live_admin_files_and_clears_a_temp_compat_pin()
    {
        var (pack, server) = PackAndServer(stampMeta: false);
        WriteSlim(NorcomPin.PinPath(server), HeadPin, "26.3", "0.20.1");
        WriteFull(server, HeadPin, "26.3", "0.20.1");
        Directory.CreateDirectory(Path.Combine(pack, "meta"));
        var compat = Path.Combine(pack, "meta", "server-compat.json");
        File.WriteAllText(compat, "{\"schema\":1,\"packVersion\":\"1.0.35\",\"indexSha256\":\"abc\",\"both\":{\"" + NorcomPin.MarbleJarName + "\":\"" + Marble + "\"},\"pin\":\"TEMP\",\"mc\":\"9.9\",\"loader\":\"0.0.1\"}");
        var hint = Path.Combine(pack, "meta", "heal-hint.json");
        File.WriteAllText(hint, "{\"pin\":\"TEMP\",\"mc\":\"9.9\",\"loader\":\"0.0.1\",\"minLauncher\":\"1.0.0\",\"repairAction\":\"leave-me\",\"jar_count\":87,\"source\":\"admin\"}");

        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.Null(block);
        Assert.Equal(File.ReadAllText(NorcomPin.FullLedgerPath(server)), File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")));
        Assert.Contains(HeadPin, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        Assert.Contains(Marble, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(NorcomPin.PinPath(server)), File.ReadAllText(Path.Combine(pack, "meta", "server-pin.json")));
        var compatText = File.ReadAllText(compat);
        Assert.Contains(HeadPin, compatText, StringComparison.Ordinal);
        Assert.Contains("1.0.35", compatText, StringComparison.Ordinal);
        Assert.Contains(NorcomPin.MarbleJarName, compatText, StringComparison.Ordinal);
        Assert.DoesNotContain("TEMP", compatText, StringComparison.Ordinal);
        var hintText = File.ReadAllText(hint);
        Assert.Contains(HeadPin, hintText, StringComparison.Ordinal);
        Assert.Contains("\"repairAction\":\"resync-pack\"", hintText, StringComparison.Ordinal);
        Assert.Contains("\"minLauncher\":\"2.0.1\"", hintText, StringComparison.Ordinal);
        Assert.Contains("\"jar_count\":87", hintText, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"admin\"", hintText, StringComparison.Ordinal);
        Cleanup(pack, server);
    }

    [Fact]
    public void Publish_derives_the_slim_pin_when_the_server_pin_is_missing()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.Delete(NorcomPin.PinPath(server));
        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.Null(block);
        var slim = JsonSerializer.Deserialize<ServerPinFile>(File.ReadAllText(Path.Combine(pack, "meta", "server-pin.json")));
        Assert.Equal(1, slim!.schema);
        Assert.Equal(Pin, slim.pin);
        Assert.Equal("26.3", slim.mc);
        Assert.Equal("0.20.1", slim.loader);
        Assert.Contains(Marble, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        Cleanup(pack, server);
    }

    [Fact]
    public void Publish_refuses_when_the_slim_pin_disagrees_with_the_live_ledger()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        WriteSlim(NorcomPin.PinPath(server), new string('b', 64), "26.3", "0.20.1");
        var before = File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json"));
        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.NotNull(block);
        Assert.Contains("disagrees", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was pushed", block, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")));
        Cleanup(pack, server);
    }

    [Fact]
    public void Publish_refuses_when_the_live_ledger_has_no_marble_jar()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.WriteAllText(NorcomPin.FullLedgerPath(server), "{\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + Pin + "\",\"jars\":[{\"name\":\"kept.jar\",\"sha256\":\"abc\",\"size\":1}]}");
        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.NotNull(block);
        Assert.Contains(NorcomPin.MarbleJarName, block, StringComparison.Ordinal);
        Assert.Contains("Nothing was pushed", block, StringComparison.Ordinal);
        Cleanup(pack, server);
    }

    [Fact]
    public void AutoFix_jar_move_does_not_rewrite_the_server_pin()
    {
        var root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "mods", "badmod.jar"), "new");
        Directory.CreateDirectory(Path.Combine(root, "mods-backup"));
        File.WriteAllText(Path.Combine(root, "mods-backup", "badmod.jar"), "old");
        WriteSlim(NorcomPin.PinPath(root), Pin, "26.3", "0.20.1");
        var before = File.ReadAllText(NorcomPin.PinPath(root));

        var moved = AutoFix.RollbackBadMod(root, "crash loading badmod.jar from mods\n");

        Assert.Equal("badmod.jar", moved);
        Assert.Equal(before, File.ReadAllText(NorcomPin.PinPath(root)));
        var ping = File.ReadAllText(NorcomPin.PingPath(root));
        Assert.Contains("badmod.jar", ping, StringComparison.Ordinal);
        Assert.Contains("regenerate the pin", ping, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    private static (string Pack, string Server) PackAndServer(bool stampMeta)
    {
        var pack = Temp();
        Directory.CreateDirectory(Path.Combine(pack, "launcher"));
        Directory.CreateDirectory(Path.Combine(pack, "meta"));
        File.WriteAllText(Path.Combine(pack, "pack.toml"), "name = \"NORCOM\"\n");
        File.WriteAllText(Path.Combine(pack, "index.toml"), "[[files]]\nfile = \"pack.toml\"\n");
        var server = Temp();
        Directory.CreateDirectory(server);
        if (stampMeta)
        {
            WriteSlim(NorcomPin.PinPath(server), Pin, "26.3", "0.20.1");
            WriteFull(server, Pin, "26.3", "0.20.1");
            WriteSlim(Path.Combine(pack, "meta", "server-pin.json"), Pin, "26.3", "0.20.1");
            File.WriteAllText(Path.Combine(pack, "meta", "server-compat.json"), JsonSerializer.Serialize(new ServerCompatFile
            {
                packVersion = "1.0.35",
                indexSha256 = "abc",
                both = "both",
                pin = Pin,
                mc = "26.3",
                loader = "0.20.1"
            }));
            File.WriteAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json"), "{\"jars\":[]}");
        }

        return (pack, server);
    }

    private static void WriteFull(string server, string pin, string mc, string loader)
    {
        var path = NorcomPin.FullLedgerPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schema\":1,\"minecraft\":\"" + mc + "\",\"fabric_loader\":\"" + loader + "\",\"pin_sha256\":\"" + pin + "\",\"jars\":[{\"name\":\"" + NorcomPin.MarbleJarName + "\",\"sha256\":\"" + Marble + "\",\"size\":4},{\"name\":\"kept.jar\",\"sha256\":\"abc\",\"size\":1}]}");
    }

    private static string ProofServer()
    {
        var server = Temp();
        WriteFull(server, Pin, "26.3", "0.20.1");
        return server;
    }

    private static BodyRemote ContentsRemote(string pin, string marble, string publishedPin) =>
        new((url, _) =>
        {
            if (url.Contains("/norcom-pack/main/", StringComparison.Ordinal))
            {
                return (200, LedgerJson(pin, Marble));
            }

            if (!url.Contains("api.github.com", StringComparison.Ordinal) && !url.Contains(Commit, StringComparison.Ordinal))
            {
                return (404, "");
            }

            return (200, Wrap(FileJson(url, publishedPin, marble)));
        });

    private static string Wrap(string json) =>
        "{\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)) + "\"}";

    private static string LedgerJson(string pin, string marble) =>
        "{\"schema\":1,\"minecraft\":\"26.3\",\"fabric_loader\":\"0.20.1\",\"pin_sha256\":\"" + pin + "\",\"jars\":[{\"name\":\"" + NorcomPin.MarbleJarName + "\",\"sha256\":\"" + marble + "\",\"size\":1}]}";

    private static string FileJson(string url, string pin, string marble)
    {
        if (url.Contains("norcom-mod-pin.json", StringComparison.Ordinal))
        {
            return LedgerJson(pin, marble);
        }

        if (url.Contains("heal-hint.json", StringComparison.Ordinal))
        {
            return "{\"pin\":\"" + pin + "\",\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"minLauncher\":\"2.0.1\",\"repairAction\":\"resync-pack\"}";
        }

        if (url.Contains("server-compat.json", StringComparison.Ordinal))
        {
            return "{\"pin\":\"" + pin + "\",\"mc\":\"26.3\",\"loader\":\"0.20.1\"}";
        }

        return "{\"schema\":1,\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"pin\":\"" + pin + "\"}";
    }

    private static void WriteSlim(string path, string pin, string mc, string loader)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new ServerPinFile
        {
            schema = 1,
            mc = mc,
            loader = loader,
            pin = pin,
            source = "server",
            updatedUtc = "2026-11-06T02:04:49Z"
        }));
    }

    private static void Cleanup(string pack, string server)
    {
        Directory.Delete(pack, true);
        Directory.Delete(server, true);
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "norcom-pin-" + Guid.NewGuid().ToString("N"));

    private sealed class OkRemote : IRemoteMetaCheck
    {
        public Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken) => Task.FromResult(200);

        public Task<RemoteBody> ReadAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(new RemoteBody(200, ""));
    }

    private sealed class BodyRemote : IRemoteMetaCheck
    {
        private readonly Func<string, CancellationToken, (int Status, string Body)> _handler;

        public BodyRemote(Func<string, CancellationToken, (int Status, string Body)> handler) => _handler = handler;

        public List<string> Urls { get; } = new();

        public Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(url, cancellationToken).Status);

        public Task<RemoteBody> ReadAsync(string url, CancellationToken cancellationToken)
        {
            Urls.Add(url);
            var (status, body) = _handler(url, cancellationToken);
            return Task.FromResult(new RemoteBody(status, body));
        }
    }

    private sealed class PushGit : ICommandRunner
    {
        public List<string> Calls { get; } = new();

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
        {
            var text = string.Join(' ', args);
            Calls.Add(text);
            if (text.StartsWith("status --porcelain", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, " M meta/norcom-mod-pin.json\n", ""));
            }

            if (text.StartsWith("remote get-url", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "https://github.com/Jefftey97/norcom-pack.git\n", ""));
            }

            if (text.StartsWith("rev-parse --abbrev-ref", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "main\n", ""));
            }

            if (text.StartsWith("rev-parse --is-inside-work-tree", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "true\n", ""));
            }

            if (text.StartsWith("diff --cached --name-status", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "M\tmeta/norcom-mod-pin.json\n", ""));
            }

            if (text.StartsWith("diff --cached --name-only", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "meta/norcom-mod-pin.json\n", ""));
            }

            if (text.StartsWith("rev-parse HEAD", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, Commit + "\n", ""));
            }

            if (text.StartsWith("add ", StringComparison.Ordinal) ||
                text.StartsWith("-c ", StringComparison.Ordinal) ||
                text.StartsWith("pull ", StringComparison.Ordinal) ||
                text.StartsWith("push ", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "", ""));
            }

            return Task.FromResult(new CommandResult(1, "", "unexpected " + text));
        }
    }

    private sealed class RecordingGit : ICommandRunner
    {
        public List<string> Calls { get; } = new();

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
        {
            Calls.Add(string.Join(' ', args));
            return Task.FromResult(new CommandResult(1, "", "git should not have been called"));
        }
    }
}
