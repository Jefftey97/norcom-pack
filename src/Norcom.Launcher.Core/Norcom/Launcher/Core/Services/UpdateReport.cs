using System;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class UpdateReport
{
	public const string HashMismatch = "rejected hash-mismatch";

	public const string PayloadVersion = "rejected payload-version";

	public const string AlreadyApplied = "rejected already-applied";

	public const string LegacyPackage = "rejected legacy-package";

	public const string MissingExe = "rejected missing-exe";

	public const string NotHttps = "rejected not-https";

	public const string UpToDate = "up-to-date";

	public const string RolledBack = "rolled-back";

	public const string NoManifest = "no-manifest";

	public static string Applied(string version)
	{
		return "applied " + version.Trim();
	}

	public static void Write(string launcherDir, string token)
	{
		Directory.CreateDirectory(launcherDir);
		File.WriteAllText(Path.Combine(launcherDir, "update-status.txt"), token.Trim() + Environment.NewLine);
		LauncherLog.Write("update " + token.Trim());
	}

	public static bool ClearRejected(string launcherDir)
	{
		string path = Path.Combine(launcherDir, "update-status.txt");
		if (!File.Exists(path))
		{
			return false;
		}
		string text = File.ReadAllText(path).Trim();
		if (!text.StartsWith("rejected", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		File.Delete(path);
		LauncherLog.Write("update cleared stale " + text);
		return true;
	}

	public static string? ReadAppliedSha(string launcherDir)
	{
		string path = Path.Combine(launcherDir, "update-applied.sha");
		if (!File.Exists(path))
		{
			return null;
		}
		string text = File.ReadAllText(path).Trim();
		if (text.Length != 64)
		{
			return null;
		}
		return text;
	}

	public static bool SameSha(string? applied, string? manifest)
	{
		if (!string.IsNullOrWhiteSpace(applied) && !string.IsNullOrWhiteSpace(manifest))
		{
			return applied.Trim().Equals(manifest.Trim(), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool SkipApplied(string? appliedSha, string? manifestSha, string? runningVersion, string? manifestVersion, string? primaryVersion, bool runningFromAlt)
	{
		if (!SameSha(appliedSha, manifestSha))
		{
			return false;
		}
		if (VersionCompare.Compare(manifestVersion, runningVersion) > 0)
		{
			return false;
		}
		if (runningFromAlt && VersionCompare.Compare(manifestVersion, string.IsNullOrWhiteSpace(primaryVersion) ? "0" : primaryVersion) > 0)
		{
			return false;
		}
		return true;
	}

	public static string? Rejection(string running, string manifestVersion, string? payloadVersion, string? exeFileVersion, string? appliedSha, string manifestSha)
	{
		if (SameSha(appliedSha, manifestSha))
		{
			return "rejected already-applied";
		}
		if (!PayloadStamp.Identical(payloadVersion, manifestVersion))
		{
			return "rejected payload-version";
		}
		if (VersionCompare.Compare(payloadVersion, running) <= 0)
		{
			return "rejected payload-version";
		}
		if (!PayloadStamp.Identical(exeFileVersion, payloadVersion))
		{
			return "rejected payload-version";
		}
		return null;
	}

	public static bool SameRelease(string? a, string? b)
	{
		Version version = VersionCompare.ToVersion(a);
		Version version2 = VersionCompare.ToVersion(b);
		if (version.Major == version2.Major && version.Minor == version2.Minor)
		{
			return Math.Max(version.Build, 0) == Math.Max(version2.Build, 0);
		}
		return false;
	}
}
