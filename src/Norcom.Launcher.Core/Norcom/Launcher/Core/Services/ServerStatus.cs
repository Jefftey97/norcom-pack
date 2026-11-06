using System.Collections.Generic;

namespace Norcom.Launcher.Core.Services;

public sealed record ServerStatus(bool Online, int Players, int Max, string Motd, int LatencyMs, IReadOnlyList<string> Names, string Error);
