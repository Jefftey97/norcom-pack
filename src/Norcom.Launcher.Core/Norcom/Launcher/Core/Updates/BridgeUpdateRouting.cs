#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Updates;

public static class BridgeUpdateRouting
{
    // Installed by the executable entry point before any legacy services are created.
    public static Func<CancellationToken,Task<bool>>? Schedule { get; set; }
}
