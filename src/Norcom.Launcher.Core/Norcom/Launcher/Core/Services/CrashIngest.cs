using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class CrashIngest
{
	public const string EnvironmentVariable = "NORCOM_CRASH_INGEST";

	public const string ReleaseUrl = "https://skaahanzi.tailefd7c9.ts.net/v1/crash";

	public const string QueuedMessage = "A report was saved for NORCOM and is queued to be sent. It has not been sent yet.";

	public static string? Resolve(string? environmentUrl, string? releaseUrl)
	{
		if (IsAllowed(environmentUrl))
		{
			return environmentUrl.Trim();
		}
		if (IsAllowed(releaseUrl))
		{
			return releaseUrl.Trim();
		}
		return null;
	}

	public static bool IsAllowed(string? url)
	{
		if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri result))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(result.UserInfo))
		{
			return false;
		}
		if (!result.AbsolutePath.Equals("/v1/crash", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (result.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (result.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
		{
			if (!result.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
			{
				return result.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
		return false;
	}

	public static string ReadLogTail(string? logPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
			{
				return "";
			}
			string[] array = File.ReadAllLines(logPath);
			int count = Math.Max(0, array.Length - 40);
			return string.Join('\n', array.Skip(count));
		}
		catch
		{
			return "";
		}
	}

	public static string BuildBody(string reportJson, string windowsVersion, string? logTail)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(reportJson);
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement value;
		string text = Redactor.ForWire(rootElement.TryGetProperty("message", out value) ? value.GetString() : "", 500);
		JsonElement value2;
		string text2 = (rootElement.TryGetProperty("source", out value2) ? (value2.GetString() ?? "ui") : "ui");
		bool flag;
		switch (text2)
		{
		case "ui":
		case "unhandled":
		case "manual":
		case "play":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			text2 = "ui";
		}
		JsonElement value3;
		string text3 = (rootElement.TryGetProperty("version", out value3) ? (value3.GetString() ?? NorcomVersion.Current) : NorcomVersion.Current);
		JsonElement value4;
		string text4 = (rootElement.TryGetProperty("utc", out value4) ? (value4.GetString() ?? "") : "");
		string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text3 + "|" + text2 + "|" + text + "|" + text4))).ToLowerInvariant();
		string log = Redactor.ForWire(logTail, 2000);
		string windows = Redactor.ForWire(windowsVersion, 40);
		return JsonSerializer.Serialize(new
		{
			id = id,
			product = "NORCOM Launcher",
			version = text3,
			windows = windows,
			source = text2,
			message = text,
			log = log
		});
	}
}
