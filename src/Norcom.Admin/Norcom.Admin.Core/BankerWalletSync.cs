using System.Diagnostics;

namespace Norcom.Admin.Core;

public readonly record struct WalletSyncResult(bool Ok, string Warning, string Output);

public static class BankerWalletSync
{
    public static string ScriptPath(string serverDirectory) =>
        Path.Combine(serverDirectory, "admin", "banker-wallet-sync.ps1");

    public static WalletSyncResult Run(string serverDirectory)
    {
        var script = ScriptPath(serverDirectory);
        if (!File.Exists(script))
        {
            return new WalletSyncResult(false, "Wallet sync script was not found. The eco change was kept.", "");
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = Path.GetDirectoryName(script) ?? serverDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(script);
            info.ArgumentList.Add("-Force");

            using var process = Process.Start(info);
            if (process == null)
            {
                return new WalletSyncResult(false, "Wallet sync could not start. The eco change was kept.", "");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return new WalletSyncResult(false, "Wallet sync did not finish. The eco change was kept.", Join(stdout, stderr));
            }

            var output = Join(stdout, stderr);
            if (process.ExitCode != 0)
            {
                return new WalletSyncResult(false, "Wallet sync exited " + process.ExitCode + ". The eco change was kept.", output);
            }

            return new WalletSyncResult(true, "", output);
        }
        catch (Exception ex)
        {
            return new WalletSyncResult(false, "Wallet sync could not start. The eco change was kept.", ex.GetType().Name);
        }
    }

    private static string Join(string stdout, string stderr)
    {
        var text = (stdout + " " + stderr).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 500 ? text[..500] : text;
    }
}
