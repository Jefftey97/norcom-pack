namespace Norcom.Admin.Core;

public sealed record ServerOverview(
    bool FolderExists,
    string OnlineMode,
    string Motd,
    string Port,
    string MaxPlayers,
    string WhiteList,
    string ViewDistance,
    string SimulationDistance,
    IReadOnlyList<string> Whitelist,
    IReadOnlyList<string> Ops,
    int CrashReports,
    int Backups,
    string Tunnel,
    string TickLine,
    string Features);
