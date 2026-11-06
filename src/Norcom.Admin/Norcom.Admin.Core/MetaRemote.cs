namespace Norcom.Admin.Core;

public interface IRemoteMetaCheck
{
    Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken);
}

public sealed class HttpRemoteMetaCheck : IRemoteMetaCheck
{
    public async Task<int> StatusCodeAsync(string url, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        return (int)response.StatusCode;
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

    public static async Task<bool> SameAsOriginMain(string packDirectory, ICommandRunner git, CancellationToken cancellationToken)
    {
        foreach (var relative in Files)
        {
            var local = await git.RunAsync("git", ["hash-object", relative], packDirectory, cancellationToken).ConfigureAwait(false);
            var remote = await git.RunAsync("git", ["rev-parse", "origin/main:" + relative], packDirectory, cancellationToken).ConfigureAwait(false);
            if (local.ExitCode != 0 || remote.ExitCode != 0)
            {
                return false;
            }

            if (!string.Equals(local.Stdout.Trim(), remote.Stdout.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task<(int Ok, int Total, string Report)> VerifyCommitAsync(
        string sha,
        string pin,
        string indexSha,
        IRemoteMetaCheck remote,
        CancellationToken cancellationToken)
    {
        var ok = 0;
        foreach (var relative in Files)
        {
            var url = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/" + sha + "/" + relative;
            var code = await remote.StatusCodeAsync(url, cancellationToken).ConfigureAwait(false);
            if (code == 200)
            {
                ok++;
            }
        }

        var report = "commit " + sha + " pin " + NorcomPin.Prefix(pin) + " index " + NorcomPin.Prefix(indexSha) + " HTTP 200×" + ok;
        return (ok, Files.Length, report);
    }
}
