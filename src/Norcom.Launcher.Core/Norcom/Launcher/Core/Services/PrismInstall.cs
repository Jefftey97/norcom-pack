using System.Collections.Generic;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public static class PrismInstall
{
	public const string EnvironmentVariable = "NORCOM_PRISM_EXE";

	public const string DownloadPage = "https://prismlauncher.org/download/";

	public static string? Find(string? environmentOverride, IEnumerable<string> candidates)
	{
		if (!string.IsNullOrWhiteSpace(environmentOverride))
		{
			string path = environmentOverride.Trim();
			if (!File.Exists(path))
			{
				return null;
			}
			return Path.GetFullPath(path);
		}
		foreach (string candidate in candidates)
		{
			if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}
		return null;
	}

	public static IEnumerable<string> Candidates(string localAppData, string programFiles)
	{
		yield return Path.Combine(localAppData, "Programs", "PrismLauncher", "prismlauncher.exe");
		yield return Path.Combine(programFiles, "Prism Launcher", "prismlauncher.exe");
		yield return Path.Combine(localAppData, "PrismLauncher", "prismlauncher.exe");
	}
}
