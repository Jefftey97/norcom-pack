using System.Diagnostics;

namespace Norcom.Admin.Core;

public sealed class ServerSession
{
    private Process? _process;

    public bool IsTrackingAlive => _process is { HasExited: false };

    public int? TrackedPid => IsTrackingAlive ? _process!.Id : null;

    public DateTime? StartedAt
    {
        get
        {
            try
            {
                return IsTrackingAlive ? _process!.StartTime : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public StartPlan Inspect(string serverDirectory, int memoryMb) =>
        ServerCommands.PlanStart(serverDirectory, memoryMb);

    public string Start(string serverDirectory, int memoryMb, StartReason reason)
    {
        var plan = ServerCommands.PlanStart(serverDirectory, memoryMb);
        if (plan.Kind == StartKind.Missing)
        {
            return Tag(reason, plan.Message);
        }

        if (IsTrackingAlive)
        {
            return Tag(reason, "The server process this panel started is already running.");
        }

        if (plan.Kind == StartKind.Script && !OperatingSystem.IsWindows() && plan.FilePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return Tag(reason, "The start script is a Windows batch file. Run Admin on Jeffrey's PC to launch it.");
        }

        var info = new ProcessStartInfo
        {
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        if (plan.Kind == StartKind.Script)
        {
            info.FileName = plan.FilePath;
        }
        else
        {
            info.FileName = "java";
            foreach (var argument in plan.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
        }

        _process = Process.Start(info) ?? throw new InvalidOperationException("Windows did not start the server process.");
        try
        {
            File.WriteAllText(Path.Combine(serverDirectory, "norcom-admin.pid"), _process.Id.ToString());
        }
        catch
        {
        }

        return Tag(reason, "Server process started (pid " + _process.Id + ").");
    }

    public async Task<string> StopAsync(
        string host,
        int port,
        string password,
        RconClient rcon,
        CancellationToken cancellationToken,
        StartReason reason,
        bool allowKill = false)
    {
        if (password.Length > 0)
        {
            try
            {
                await rcon.CommandAsync(host, port, password, "stop", cancellationToken).ConfigureAwait(false);
                return Tag(reason, "Stop sent over RCON.");
            }
            catch (Exception ex)
            {
                if (_process is not { HasExited: false })
                {
                    return Tag(reason, "RCON stop failed. " + ex.Message);
                }
            }
        }

        if (_process is { HasExited: false })
        {
            try
            {
                await _process.StandardInput.WriteLineAsync("stop".AsMemory(), cancellationToken).ConfigureAwait(false);
                return Tag(reason, "Stop written to the server console.");
            }
            catch
            {
                if (!allowKill)
                {
                    return Tag(reason, "Stop could not be written. The process was left running.");
                }

                _process.Kill(entireProcessTree: false);
                return Tag(reason, "Hung process was closed after stop could not be written.");
            }
        }

        return Tag(reason, password.Length == 0
            ? "No RCON password is loaded, and this panel is not tracking a server process."
            : "Stop was not delivered.");
    }

    public async Task<bool> WaitUntilQuiet(string serverDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (!IsTrackingAlive && !ServerCommands.JavaAppearsUp(serverDirectory))
            {
                return true;
            }

            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
        }

        return !IsTrackingAlive && !ServerCommands.JavaAppearsUp(serverDirectory);
    }

    public static string Tag(StartReason reason, string message) =>
        "(" + reason.ToString().ToLowerInvariant() + ") " + message;
}
