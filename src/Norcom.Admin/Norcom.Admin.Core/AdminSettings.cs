using System.Text.Json;

namespace Norcom.Admin.Core;

public sealed class AdminSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public const int LiveHeapMb = 8192;

    public string ServerPath { get; set; } = @"G:\MinecraftServer";

    public string PackPath { get; set; } = @"G:\MinecraftServer\norcom-pack";

    public string RconHost { get; set; } = "127.0.0.1";

    public int RconPort { get; set; } = 25575;

    public int DayTicks { get; set; } = 12000;

    public int NightTicks { get; set; } = 12000;

    public int MemoryMb { get; set; } = LiveHeapMb;

    public AutoFixOptions AutoFix { get; set; } = new();

    public string[] SchedulerAllow { get; set; } = SchedulerPolicy.Allow.ToArray();

    public string[] SchedulerDeny { get; set; } = SchedulerPolicy.Deny.ToArray();

    public static AdminSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AdminSettings>(File.ReadAllText(path), JsonOpts);
                if (settings != null)
                {
                    settings.MemoryMb = LiveHeapMb;
                    settings.RconHost = "127.0.0.1";
                    settings.DayTicks = Math.Clamp(settings.DayTicks, 1000, 72000);
                    settings.NightTicks = Math.Clamp(settings.NightTicks, 1000, 72000);
                    settings.RconPort = Math.Clamp(settings.RconPort, 1, 65535);
                    settings.AutoFix ??= new AutoFixOptions();
                    settings.LockSchedulerPolicy();
                    return settings;
                }
            }
        }
        catch
        {
        }

        return new AdminSettings();
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        MemoryMb = LiveHeapMb;
        RconHost = "127.0.0.1";
        LockSchedulerPolicy();
        var text = JsonSerializer.Serialize(this, JsonOpts);
        if (text.Contains("rcon.password", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("\"password\"", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ghp_", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("github_pat_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to write a password or token into admin settings.");
        }

        File.WriteAllText(path, text);
    }

    public void LockSchedulerPolicy()
    {
        SchedulerAllow = SchedulerPolicy.Allow.ToArray();
        SchedulerDeny = SchedulerPolicy.Deny.ToArray();
    }
}
