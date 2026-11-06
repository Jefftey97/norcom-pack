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

        var pinBlock = NorcomPin.PublishBlock(packDirectory, serverDirectory);
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

        if (GitSafety.DeletesLauncher(staged.Stdout))
        {
            await Git(packDirectory, ["reset"], cancellationToken).ConfigureAwait(false);
            return Fail("Refusing to publish because launcher/ was staged for deletion. The index was unstaged.");
        }

        if (!string.IsNullOrWhiteSpace(staged.Stdout))
        {
            var commit = await Git(packDirectory, ["commit", "-m", "Update NORCOM pack"], cancellationToken).ConfigureAwait(false);
            if (commit.ExitCode != 0)
            {
                return Fail("git commit failed. " + Tail(commit));
            }
        }

        var pull = await Git(packDirectory, ["pull", "--ff-only", "origin", "main"], cancellationToken).ConfigureAwait(false);
        if (pull.ExitCode != 0)
        {
            return Fail("Fast-forward pull failed, so nothing was pushed. " + Tail(pull));
        }

        var push = await Git(packDirectory, GitSafety.PushArguments(), cancellationToken).ConfigureAwait(false);
        if (push.ExitCode != 0)
        {
            return Fail("Push was rejected. The remote was not force-updated. " + Tail(push));
        }

        return new PublishResult(true, true, "Published to Jefftey97/norcom-pack on main. launcher/ was kept, and installer-version.txt is not in the packwiz index.");
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
