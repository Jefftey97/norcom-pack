using System.Diagnostics;

namespace Norcom.Admin.Core;

public static class SchedulerPolicy
{
    public static readonly string[] Allow = ["watch-flag", "MSPT sample", "crash-inbox scan"];

    public static readonly string[] Deny = ["GH push", "Discord", "jar delete", "SoT rewrite", "silent publish"];

    public static bool IsAllowed(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) &&
        Allow.Contains(kind.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !Deny.Contains(kind.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Describe()
    {
        var policy = "Allowed scheduled work: " + string.Join(", ", Allow) + ". Denied: " + string.Join(", ", Deny) + ".";
        if (!OperatingSystem.IsWindows())
        {
            return policy + " Task last-run is queried on Windows only.";
        }

        try
        {
            var info = new ProcessStartInfo("schtasks", "/Query /FO LIST /V")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(info);
            if (process == null)
            {
                return policy + " Could not query Task Scheduler.";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            var hit = output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line =>
                line.Contains("NORCOM", StringComparison.OrdinalIgnoreCase) &&
                (line.StartsWith("TaskName", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Task Name", StringComparison.OrdinalIgnoreCase)));
            return hit == null
                ? policy + " No NORCOM scheduled task is registered."
                : policy + " " + hit;
        }
        catch
        {
            return policy + " Task Scheduler query failed.";
        }
    }
}
