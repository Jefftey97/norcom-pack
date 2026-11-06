using System.Collections.Generic;

namespace Norcom.Launcher.Core.Services;

public sealed record LaunchPlan(string MainClass, IReadOnlyList<string> JvmArgs, IReadOnlyList<string> GameArgs, IReadOnlyList<LibraryFile> Libraries, string AssetsIndexId);
