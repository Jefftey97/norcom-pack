using System;
using System.IO;
using Norcom.Launcher.Core;

namespace Norcom.Launcher.Core.Services;

public static class PayloadStamp
{
	public const string FileName = "payload-version.txt";

	public static string ForProduct(string? productVersion)
	{
		return NorcomVersion.Normalize(productVersion);
	}

	public static bool Identical(string? payloadVersion, string? productVersion)
	{
		if (string.IsNullOrWhiteSpace(payloadVersion) || string.IsNullOrWhiteSpace(productVersion))
		{
			return false;
		}
		return string.Equals(ForProduct(payloadVersion), ForProduct(productVersion), StringComparison.Ordinal);
	}

	public static void WriteBeside(string exePath, string productVersion)
	{
		string stamp = ForProduct(productVersion);
		if (!Identical(stamp, productVersion))
		{
			throw new InvalidOperationException("payload-version must match ProductVersion.");
		}
		string? directory = Path.GetDirectoryName(Path.GetFullPath(exePath));
		if (string.IsNullOrWhiteSpace(directory))
		{
			throw new InvalidOperationException("payload-version directory is missing.");
		}
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, FileName), stamp + Environment.NewLine);
	}
}
