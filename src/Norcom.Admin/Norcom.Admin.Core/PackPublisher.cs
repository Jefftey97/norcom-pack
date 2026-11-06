namespace Norcom.Admin.Core;

public sealed class PackPublisher
{
    private readonly ICommandRunner _git;
    private readonly SecretRedactor _redactor;

    public PackPublisher(ICommandRunner git, SecretRedactor redactor)
    {
        _git = git;
        _redactor = redactor;
    }

    public async Task<PublishResult> PublishAsync(string packDirectory, string serverDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packDirectory) || !Directory.Exists(packDirectory))
        {
            return Fail("Pack folder was not found. Set it to the local Jefftey97/norcom-pack checkout.");
        }

        if (!File.Exists(Path.Combine(packDirectory, "pack.toml")) || !File.Exists(Path.Combine(packDirectory, "index.toml")))
        {
            return Fail("That folder is not a packwiz pack (pack.toml and index.toml are required).");
        }

        if (!Directory.Exists(Path.Combine(packDirectory, "launcher")))
        {
            return Fail("Refusing to publish because launcher/ is missing.");
        }

        var pinBlock = NorcomPin.StampFriendsMeta(packDirectory, serverDirectory);
        if (pinBlock != null)
        {
            return Fail(pinBlock);
        }

        var indexPath = Path.Combine(packDirectory, "index.toml");
        var original = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
        var stripped = IndexGuard.StripInstallerVersion(original);
        if (!string.Equals(original, stripped, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(indexPath, stripped, cancellationToken).ConfigureAwait(false);
        }

        var status = await Git(packDirectory, ["status", "--porcelain"], cancellationToken).ConfigureAwait(false);
        if (status.ExitCode != 0)
        {
            return Fail("git status failed. " + Tail(status));
        }

        if (GitSafety.DeletesLauncher(status.Stdout))
        {
            return Fail("Refusing to publish because launcher/ would be deleted.");
        }

        if (GitSafety.DropsPackMeta(status.Stdout))
        {
            return Fail("Refusing to publish because pin meta would be deleted. Nothing was pushed.");
        }

        var remote = await Git(packDirectory, ["remote", "get-url", "origin"], cancellationToken).ConfigureAwait(false);
        if (remote.ExitCode != 0 || !remote.Stdout.Contains("Jefftey97/norcom-pack", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Origin is not Jefftey97/norcom-pack. Nothing was pushed.");
        }

        var branch = await Git(packDirectory, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (branch.ExitCode != 0 || !branch.Stdout.Trim().Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Publish only runs from the main branch. Nothing was pushed.");
        }

        var repoBlock = await RequireGitRepo(packDirectory, cancellationToken).ConfigureAwait(false);
        if (repoBlock != null)
        {
            return Fail(repoBlock);
        }

        var add = await Git(packDirectory, ["add", "-A"], cancellationToken).ConfigureAwait(false);
        if (add.ExitCode != 0)
        {
            return Fail("git add failed. " + Tail(add));
        }

        var staged = await Git(packDirectory, ["diff", "--cached", "--name-status"], cancellationToken).ConfigureAwait(false);
        if (staged.ExitCode != 0)
        {
            return Fail("Could not read the staged files. " + Tail(staged));
        }

        if (GitSafety.DeletesLauncher(staged.Stdout) || GitSafety.DropsPackMeta(staged.Stdout))
        {
            await Git(packDirectory, ["reset"], cancellationToken).ConfigureAwait(false);
            return Fail("Refusing to publish because launcher/ or pin meta was staged for deletion. The index was unstaged.");
        }

        if (!string.IsNullOrWhiteSpace(staged.Stdout))
        {
            var commit = await Git(packDirectory, CommitArgs("Update NORCOM pack"), cancellationToken).ConfigureAwait(false);
            if (commit.ExitCode != 0)
            {
                return Fail("git commit failed. " + Tail(commit));
            }
        }

        var pull = await Git(packDirectory, ["pull", "--ff-only", "origin", "main"], cancellationToken).ConfigureAwait(false);
        if (pull.ExitCode != 0)
        {
            return Fail("This checkout has diverged from origin/main. The fast-forward was refused, so nothing was pushed. " + Tail(pull));
        }

        var push = await Git(packDirectory, GitSafety.PushArguments(), cancellationToken).ConfigureAwait(false);
        if (push.ExitCode != 0)
        {
            return Fail("Push was rejected. The remote was not force-updated. " + Tail(push));
        }

        return new PublishResult(true, true, "Packwiz config Publish finished with a fast-forward push. launcher/ stayed. Nothing was force-pushed.");
    }

    public async Task<PublishResult> PublishMetaAsync(string packDirectory, string serverDirectory, IRemoteMetaCheck remote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packDirectory) || !Directory.Exists(packDirectory))
        {
            return Fail("Meta Publish refused. Pack folder was not found. Nothing was pushed.");
        }

        var pinBlock = NorcomPin.StampFriendsMeta(packDirectory, serverDirectory);
        if (pinBlock != null)
        {
            return Fail(pinBlock);
        }

        var pin = ReadField(Path.Combine(packDirectory, "meta", "server-pin.json"), "pin");
        var index = ReadField(Path.Combine(packDirectory, "meta", "server-compat.json"), "indexSha256");
        if (HealFails.BlocksMetaPublish(serverDirectory, pin))
        {
            return Fail("Meta Publish stopped. Launcher PinGate or a dual-sided heal may be the issue for this pin. Nothing was pushed.");
        }

        if (await MetaRemote.SameAsOriginMain(packDirectory, _git, cancellationToken).ConfigureAwait(false))
        {
            return new PublishResult(true, false, "ALREADY_LIVE pin " + NorcomPin.Prefix(pin) + " index " + NorcomPin.Prefix(index) + " same-as-raw: yes. Nothing was pushed.", true);
        }

        var repoBlock = await RequireGitRepo(packDirectory, cancellationToken).ConfigureAwait(false);
        if (repoBlock != null)
        {
            return Fail(repoBlock);
        }

        var add = new List<string> { "add", "--" };
        add.AddRange(MetaRemote.Files);
        var added = await Git(packDirectory, add, cancellationToken).ConfigureAwait(false);
        if (added.ExitCode != 0)
        {
            return Fail("Meta Publish git add failed. " + Tail(added));
        }

        var staged = await Git(packDirectory, ["diff", "--cached", "--name-only"], cancellationToken).ConfigureAwait(false);
        if (staged.ExitCode != 0)
        {
            return Fail("Meta Publish could not read the staged files. " + Tail(staged));
        }

        if (string.IsNullOrWhiteSpace(staged.Stdout))
        {
            return new PublishResult(true, false, "ALREADY_LIVE pin " + NorcomPin.Prefix(pin) + " index " + NorcomPin.Prefix(index) + " same-as-raw: yes. Nothing was pushed.", true);
        }

        var commit = await Git(packDirectory, CommitArgs("Update NORCOM friends meta"), cancellationToken).ConfigureAwait(false);
        if (commit.ExitCode != 0)
        {
            return Fail("Meta Publish commit failed. " + Tail(commit));
        }

        var pull = await Git(packDirectory, ["pull", "--ff-only", "origin", "main"], cancellationToken).ConfigureAwait(false);
        if (pull.ExitCode != 0)
        {
            return Fail("This checkout has diverged from origin/main. The fast-forward was refused, so nothing was pushed. " + Tail(pull));
        }

        var push = await Git(packDirectory, GitSafety.PushArguments(), cancellationToken).ConfigureAwait(false);
        if (push.ExitCode != 0)
        {
            return Fail("Push was rejected. The remote was not force-updated. " + Tail(push));
        }

        var head = await Git(packDirectory, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var sha = head.ExitCode == 0 ? head.Stdout.Trim() : "";
        var verify = await MetaRemote.VerifyCommitAsync(sha, pin, index, remote, cancellationToken).ConfigureAwait(false);
        var clear = verify.Ok == verify.Total && sha.Length > 0;
        var message = verify.Report + (clear ? " flag cleared" : " verify incomplete. Flag left in place.");
        return new PublishResult(clear, true, message, clear);
    }

    private static string ReadField(string path, string name)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string[] CommitArgs(string message) =>
    [
        "-c", "user.name=NORCOM",
        "-c", "user.email=norcom@local",
        "commit", "-m", message
    ];

    private async Task<string?> RequireGitRepo(string packDirectory, CancellationToken cancellationToken)
    {
        var inside = await Git(packDirectory, ["rev-parse", "--is-inside-work-tree"], cancellationToken).ConfigureAwait(false);
        return inside.ExitCode == 0 && inside.Stdout.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Pack folder is not a git repo. Nothing was added or pushed.";
    }

    private async Task<CommandResult> Git(string packDirectory, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        GitSafety.EnsureNoForce(args);
        return await _git.RunAsync("git", args, packDirectory, cancellationToken).ConfigureAwait(false);
    }

    private PublishResult Fail(string message) => new(false, false, _redactor.Clean(message));

    private string Tail(CommandResult result)
    {
        var text = (string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr).Trim();
        if (text.Length > 500)
        {
            text = text[^500..];
        }

        return _redactor.Clean(text);
    }
}
