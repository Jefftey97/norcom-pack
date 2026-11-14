using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

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

	/// <summary>
	/// Every place a friend's own Prism may already be, so NORCOM uses it instead of installing a
	/// second copy: Prism's own installer, Program Files (both), Scoop, any Prism Windows lists as
	/// installed, and a Prism that is open right now.
	/// </summary>
	public static IEnumerable<string> Candidates(string localAppData, string programFiles)
	{
		yield return Path.Combine(localAppData, "Programs", "PrismLauncher", "prismlauncher.exe");
		yield return Path.Combine(programFiles, "Prism Launcher", "prismlauncher.exe");
		yield return Path.Combine(localAppData, "PrismLauncher", "prismlauncher.exe");
		string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		if (!string.IsNullOrWhiteSpace(x86))
		{
			yield return Path.Combine(x86, "Prism Launcher", "prismlauncher.exe");
		}
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrWhiteSpace(home))
		{
			yield return Path.Combine(home, "scoop", "apps", "prismlauncher", "current", "prismlauncher.exe");
		}
		foreach (string listed in Registered())
		{
			yield return listed;
		}
		foreach (string running in Running())
		{
			yield return running;
		}
	}

	/// <summary>Prism entries in Windows' installed-programs list (current user and machine).</summary>
	public static IEnumerable<string> Registered()
	{
		List<string> found = new List<string>();
		if (!OperatingSystem.IsWindows())
		{
			return found;
		}
		try
		{
			foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
			{
				foreach (string root in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
				{
					using RegistryKey? uninstall = hive.OpenSubKey(root);
					if (uninstall == null)
					{
						continue;
					}
					foreach (string name in uninstall.GetSubKeyNames())
					{
						using RegistryKey? app = uninstall.OpenSubKey(name);
						string display = app?.GetValue("DisplayName") as string ?? "";
						if (!display.Contains("Prism Launcher", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						string location = (app?.GetValue("InstallLocation") as string ?? "").Trim().Trim('"');
						if (location.Length > 0)
						{
							found.Add(Path.Combine(location, "prismlauncher.exe"));
						}
						string icon = (app?.GetValue("DisplayIcon") as string ?? "").Split(',')[0].Trim().Trim('"');
						if (icon.EndsWith("prismlauncher.exe", StringComparison.OrdinalIgnoreCase))
						{
							found.Add(icon);
						}
					}
				}
			}
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
		{
		}
		return found;
	}

	private static IEnumerable<string> Running()
	{
		List<string> found = new List<string>();
		try
		{
			foreach (Process process in Process.GetProcessesByName("prismlauncher"))
			{
				using (process)
				{
					try
					{
						string? path = process.MainModule?.FileName;
						if (!string.IsNullOrWhiteSpace(path))
						{
							found.Add(path);
						}
					}
					catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
					{
					}
				}
			}
		}
		catch (InvalidOperationException)
		{
		}
		return found;
	}
}
