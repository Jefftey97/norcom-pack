namespace Norcom.Admin.Core;

public sealed class AutoFixOptions
{
    public bool FrozenServerRestart { get; set; } = true;

    public bool TunnelReconnect { get; set; }

    public bool BadModRollback { get; set; }

    public bool DiskLogCleanup { get; set; } = true;

    public bool BrokenConfigRestore { get; set; } = true;

    public bool CrashAlerts { get; set; } = true;

    public int FrozenAfterMinutes { get; set; } = 3;

    public int KeepLogsDays { get; set; } = 14;
}
