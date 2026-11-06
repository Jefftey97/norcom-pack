namespace Norcom.Admin.Core;

public static class GitSafety
{
    public const string ExpectedRemote = "Jefftey97/norcom-pack";

    public static void EnsureNoForce(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--force-with-lease", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--force", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Force push is not allowed.");
            }
        }
    }

    public static bool DeletesLauncher(string porcelain)
    {
        foreach (var line in porcelain.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2];
            var path = line[3..].Trim().Trim('"').Replace('\\', '/');
            if ((path.Equals("launcher", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("launcher/", StringComparison.OrdinalIgnoreCase)) &&
                status.Contains('D'))
            {
                return true;
            }
        }

        return false;
    }

    public static readonly string[] PackMetaFiles =
    [
        "meta/server-pin.json",
        "meta/server-compat.json",
        "meta/norcom-mod-pin.json"
    ];

    public static bool DropsPackMeta(string porcelain)
    {
        foreach (var line in porcelain.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length < 4 || !line[..2].Contains('D'))
            {
                continue;
            }

            var path = line[3..].Trim().Trim('"').Replace('\\', '/');
            if (PackMetaFiles.Any(required => path.Equals(required, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> PushArguments() => ["push", "--ff-only", "origin", "main"];
}
