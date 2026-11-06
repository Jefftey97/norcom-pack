using System.Text.Json;
using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class PinGateTests
{
    [Fact]
    public async Task Publish_refuses_when_server_pin_is_missing()
    {
        var (pack, server) = PackAndServer();
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.False(result.Ok);
        Assert.False(result.Pushed);
        Assert.Contains("missing", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was pushed", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
        Directory.Delete(pack, true);
        Directory.Delete(server, true);
    }

    [Fact]
    public async Task Publish_refuses_when_server_pin_does_not_match()
    {
        var (pack, server) = PackAndServer();
        WritePin(server, "deadbeef");
        var git = new RecordingGit();
        var result = await new PackPublisher(git, new SecretRedactor()).PublishAsync(pack, server, CancellationToken.None);
        Assert.False(result.Pushed);
        Assert.Contains("does not match", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was pushed", result.Message, StringComparison.Ordinal);
        Assert.Empty(git.Calls);
        Directory.Delete(pack, true);
        Directory.Delete(server, true);
    }

    [Fact]
    public void AutoFix_jar_move_bumps_the_server_pin()
    {
        var root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        File.WriteAllText(Path.Combine(root, "mods", "badmod.jar"), "new");
        Directory.CreateDirectory(Path.Combine(root, "mods-backup"));
        File.WriteAllText(Path.Combine(root, "mods-backup", "badmod.jar"), "old");
        WritePin(root, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "26.3", "fabric");

        var moved = AutoFix.RollbackBadMod(root, "crash loading badmod.jar from mods\n");
        Assert.Equal("badmod.jar", moved);

        var pin = NorcomPin.TryRead(root);
        Assert.NotNull(pin);
        Assert.Equal(1, pin!.schema);
        Assert.Equal("26.3", pin.mc);
        Assert.Equal("fabric", pin.loader);
        Assert.Equal(NorcomPin.AutoFixSource, pin.source);
        Assert.NotEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", pin.pin);
        Assert.False(string.IsNullOrWhiteSpace(pin.updatedUtc));
        var ping = File.ReadAllText(NorcomPin.PingPath(root));
        Assert.Contains("badmod.jar", ping, StringComparison.Ordinal);
        Assert.Contains(pin.pin, ping, StringComparison.Ordinal);
        Assert.Contains(NorcomPin.AutoFixSource, ping, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    private static (string Pack, string Server) PackAndServer()
    {
        var pack = Temp();
        Directory.CreateDirectory(Path.Combine(pack, "launcher"));
        File.WriteAllText(Path.Combine(pack, "pack.toml"), "name = \"NORCOM\"\n");
        File.WriteAllText(Path.Combine(pack, "index.toml"), "[[files]]\nfile = \"pack.toml\"\n");
        var server = Temp();
        Directory.CreateDirectory(server);
        return (pack, server);
    }

    private static void WritePin(string server, string pin, string mc = "26.3", string loader = "fabric")
    {
        var path = NorcomPin.PinPath(server);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new ServerPinFile
        {
            schema = 1,
            mc = mc,
            loader = loader,
            pin = pin,
            source = "test",
            updatedUtc = "2026-11-06T00:00:00Z"
        });
        File.WriteAllText(path, json);
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "norcom-pin-" + Guid.NewGuid().ToString("N"));

    private sealed class RecordingGit : ICommandRunner
    {
        public List<string> Calls { get; } = new();

        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
        {
            Calls.Add(fileName + " " + string.Join(' ', args));
            return Task.FromResult(new CommandResult(1, "", "git should not have been called"));
        }
    }
}
