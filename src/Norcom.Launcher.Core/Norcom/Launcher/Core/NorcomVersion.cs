using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core;

public static class NorcomVersion
{
	public const string Product = "NORCOM Launcher";

	public static readonly string Current = Resolve();

	public static readonly string UserAgent = "NORCOM-Launcher/" + Current;

	public static string Normalize(string? version)
	{
		Version version2 = VersionCompare.ToVersion(version);
		return version2.Major + "." + version2.Minor + "." + Math.Max(version2.Build, 0);
	}

	public static string? TryReadExeVersion(string? exePath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
			{
				return null;
			}
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);
			string text = (string.IsNullOrWhiteSpace(versionInfo.ProductVersion) ? versionInfo.FileVersion : versionInfo.ProductVersion);
			return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
		}
		catch
		{
			return null;
		}
	}

	private static string Resolve()
	{
		string text2 = typeof(NorcomVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (!string.IsNullOrWhiteSpace(text2))
		{
			int num = text2.IndexOf('+');
			if (num > 0)
			{
				text2 = text2.Substring(0, num);
			}
			string text3 = Normalize(text2);
			if (text3 != "0.0.0")
			{
				return text3;
			}
		}
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath) && Path.GetFileName(processPath).Equals("NorcomLauncher.exe", StringComparison.OrdinalIgnoreCase))
		{
			string text = TryReadExeVersion(processPath);
			if (text != null && text != "0.0.0")
			{
				return text;
			}
		}
		return "2.0.31";
	}
}
