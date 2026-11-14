using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

/// <summary>
/// Installs Prism Launcher for a friend who does not have it, so Play needs no manual setup.
/// The archive is Prism's own unmodified release from its official GitHub repository, pinned to
/// one version and checked against that release's published SHA-256 before anything is unpacked.
/// The self-contained (MinGW) build is used because the MSVC build needs the Visual C++ runtime,
/// which only Prism's setup program installs. The non-portable archive keeps Prism's data in
/// %APPDATA%\PrismLauncher, the folder NORCOM already uses. An existing Prism is never touched.
/// </summary>
public static class PrismBootstrap
{
	public const string Version = "11.1.1";

	public const string ArchiveUrl = "https://github.com/PrismLauncher/PrismLauncher/releases/download/11.1.1/PrismLauncher-Windows-MinGW-w64-11.1.1.zip";

	public const string ArchiveSha256 = "6c2cdfc2f31c609362f2a636c6674c766d62273be4969f5721a3fd00dc0f9b82";

	public const long ArchiveBytes = 43687345L;

	public const string ExeName = "prismlauncher.exe";

	/// <summary>The first place <see cref="PrismInstall.Candidates"/> looks, and where Prism's own setup installs.</summary>
	public static string InstallDir(string localAppData)
	{
		return Path.Combine(localAppData, "Programs", "PrismLauncher");
	}

	/// <summary>Returns true when Prism was installed by this call, false when one was already there.</summary>
	public static async Task<bool> EnsureAsync(HttpClient http, TransferMeter meter, string? environmentOverride, IEnumerable<string> candidates, string localAppData, string workDir, CancellationToken ct)
	{
		if (!string.IsNullOrWhiteSpace(environmentOverride) || PrismInstall.Find(null, candidates) != null)
		{
			return false;
		}
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Prism Launcher is installed automatically on Windows only.");
		}
		string target = InstallDir(localAppData);
		if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length > 0)
		{
			throw new IOException("The Prism Launcher folder exists but has no " + ExeName + ". It was left alone.");
		}
		meter.SetPhase("Downloading Prism Launcher", 0.22, 0.1);
		Directory.CreateDirectory(workDir);
		string zip = Path.Combine(workDir, "prism-" + Version + ".zip");
		string staging = target + ".norcom-tmp";
		try
		{
			await HttpDownload.DownloadAsync(http, ArchiveUrl, zip, meter, ct).ConfigureAwait(false);
			Verify(zip, ArchiveBytes, ArchiveSha256);
			meter.SetPhase("Installing Prism Launcher", 0.32, 0.03);
			Install(zip, staging, target);
		}
		finally
		{
			DeleteQuiet(zip);
			try
			{
				if (Directory.Exists(staging))
				{
					Directory.Delete(staging, recursive: true);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
		LauncherLog.Write("prism installed " + Version);
		return true;
	}

	public static void Verify(string zip, long expectedBytes, string expectedSha256)
	{
		long length = new FileInfo(zip).Length;
		if (length != expectedBytes)
		{
			throw new InvalidDataException("The Prism download was " + length + " bytes, expected " + expectedBytes + ". Nothing was installed.");
		}
		using FileStream stream = File.OpenRead(zip);
		string actual = Convert.ToHexString(SHA256.HashData(stream));
		if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The Prism download did not match its published checksum. Nothing was installed.");
		}
	}

	/// <summary>Unpacks beside the target, checks the result, then moves it into place in one step.</summary>
	public static void Install(string zip, string staging, string target)
	{
		if (Directory.Exists(staging))
		{
			Directory.Delete(staging, recursive: true);
		}
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		ZipFile.ExtractToDirectory(zip, staging);
		if (!File.Exists(Path.Combine(staging, ExeName)))
		{
			throw new InvalidDataException("The Prism archive had no " + ExeName + ". Nothing was installed.");
		}
		if (File.Exists(Path.Combine(staging, "portable.txt")))
		{
			throw new InvalidDataException("The Prism archive is the portable build. Nothing was installed.");
		}
		if (Directory.Exists(target))
		{
			Directory.Delete(target, recursive: false);
		}
		Directory.Move(staging, target);
	}

	/// <summary>
	/// Settings that, when blank, make a fresh Prism open its setup wizard (language, Java, theme)
	/// before it will launch anything. NORCOM already supplies Java 25 per instance, so the Java
	/// pages are not needed. Only the Microsoft sign-in page, which Prism needs, is left.
	/// Values come from Prism 11.1.1 Application::createSetupWizard.
	/// </summary>
	public static readonly (string Key, string Value)[] FirstRunSettings =
	{
		("Language", "en_US"),
		("ApplicationTheme", "dark"),
		("IconTheme", "pe_colored"),
		("IgnoreJavaWizard", "true"),
		("UserAskedAboutAutomaticJavaDownload", "true"),
	};

	/// <summary>Adds only the first-run settings that are missing or blank. Existing choices are never changed.</summary>
	public static bool SeedSettings(string cfgPath)
	{
		List<string> lines = File.Exists(cfgPath) ? new List<string>(File.ReadAllLines(cfgPath)) : new List<string>();
		Dictionary<string, int> present = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int i = 0; i < lines.Count; i++)
		{
			int eq = lines[i].IndexOf('=');
			if (eq > 0 && !lines[i].TrimStart().StartsWith('['))
			{
				present[lines[i].Substring(0, eq).Trim()] = i;
			}
		}
		int general = lines.FindIndex(l => l.Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase));
		if (general < 0)
		{
			lines.Insert(0, "[General]");
			general = 0;
			present = new Dictionary<string, int>(present.Count, StringComparer.Ordinal);
			for (int i = 1; i < lines.Count; i++)
			{
				int eq = lines[i].IndexOf('=');
				if (eq > 0)
				{
					present[lines[i].Substring(0, eq).Trim()] = i;
				}
			}
		}
		bool changed = false;
		int insertAt = general + 1;
		foreach ((string key, string value) in FirstRunSettings)
		{
			if (present.TryGetValue(key, out int at))
			{
				if (lines[at].Substring(lines[at].IndexOf('=') + 1).Trim().Length == 0)
				{
					lines[at] = key + "=" + value;
					changed = true;
				}
				continue;
			}
			lines.Insert(insertAt++, key + "=" + value);
			changed = true;
		}
		if (changed)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
			File.WriteAllLines(cfgPath, lines);
			LauncherLog.Write("prism first-run settings filled");
		}
		return changed;
	}

	private static void DeleteQuiet(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
