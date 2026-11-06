using System.Text.Json;
using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class PinGateTests
{
    private const string Pin = "07983921b11ec8e8443bb307817c7b10c90c4388491c45d05da2a819108936ac";

    [Fact]
    public async Task Publish_refuses_when_server_pin_is_missing()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.Delete(NorcomPin.PinPath(server));
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.False(result.Pushed);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task Publish_refuses_when_a_pack_meta_file_is_missing()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.Delete(Path.Combine(pack, "meta", "norcom-mod-pin.json"));
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.False(result.Pushed);
        Assert.Contains("norcom-mod-pin.json", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
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
    public async Task Publish_ignores_the_mod_ledger_when_slim_pins_match()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        File.WriteAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json"), "{\"pin\":\"ledger-is-not-the-gate\",\"jars\":[\"kept.jar\"]}");
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.Contains("git status failed", result.Message, StringComparison.Ordinal);
        Assert.Contains(Pin, File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jars", File.ReadAllText(Path.Combine(pack, "meta", "norcom-mod-pin.json")), StringComparison.Ordinal);
        Assert.NotEmpty(git.Calls);
        Assert.DoesNotContain("push", string.Join('\n', git.Calls), StringComparison.OrdinalIgnoreCase);
        Cleanup(pack, server);
    }

    [Fact]
    public async Task Meta_publish_skips_push_when_origin_main_matches()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var git = new SameHashGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishMetaAsync(pack, server, new OkRemote(), CancellationToken.None);
        Assert.True(result.ClearFlag);
        Assert.False(result.Pushed);
        Assert.Contains("ALREADY_LIVE", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("push", string.Join('\n', git.Calls), StringComparison.OrdinalIgnoreCase);
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
    public void Heal_hint_is_left_alone_when_pin_and_launcher_match()
    {
        var (pack, server) = PackAndServer(stampMeta: true);
        var hint = Path.Combine(pack, "meta", "heal-hint.json");
        File.WriteAllText(hint, "{\"pin\":\"" + Pin + "\",\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"minLauncher\":\"2.0.1\",\"repairAction\":\"leave-me\"}");
        var before = File.ReadAllText(hint);
        var block = NorcomPin.StampFriendsMeta(pack, server);
        Assert.Null(block);
        Assert.Equal(before, File.ReadAllText(hint));
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
    }

    private sealed class SameHashGit : ICommandRunner
    {
        public List<string> Calls { get; } = new();

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
        {
            var text = string.Join(' ', args);
            Calls.Add(text);
            if (text.StartsWith("hash-object", StringComparison.Ordinal) || text.StartsWith("rev-parse origin/main:", StringComparison.Ordinal))
            {
                return Task.FromResult(new CommandResult(0, "abc123\n", ""));
            }

            return Task.FromResult(new CommandResult(1, "", "unexpected"));
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
