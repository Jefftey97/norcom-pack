using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Norcom.Launcher.Core;

namespace Norcom.Launcher.Core.Services;

public static class HomeStatusClient
{
	public const string PathVariable = "NORCOM_STATUS_PATH";

	public const string UrlVariable = "NORCOM_STATUS_URL";

	public const string HostStatusPath = "G:\\MinecraftServer\\.norcom\\status.json";

	public static HomeStatus Read()
	{
		string? path = Environment.GetEnvironmentVariable(PathVariable);
		if (!string.IsNullOrWhiteSpace(path))
		{
			return FromFile(path.Trim());
		}
		string? url = Environment.GetEnvironmentVariable(UrlVariable);
		if (!string.IsNullOrWhiteSpace(url))
		{
			return FromUrl(url.Trim());
		}
		if (File.Exists(HostStatusPath))
		{
			return FromFile(HostStatusPath);
		}
		string local = Path.Combine(NorcomPaths.LauncherDir, "status.json");
		if (File.Exists(local))
		{
			return FromFile(local);
		}
		return Unknown("Server status is not published yet.");
	}

	public static HomeStatus Parse(string json, string source)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("up", out var up) || (up.ValueKind != JsonValueKind.True && up.ValueKind != JsonValueKind.False))
			{
				return Unknown("Server status file could not be read.");
			}
			int players = 0;
			if (root.TryGetProperty("players", out var count) && count.TryGetInt32(out var parsed) && parsed >= 0)
			{
				players = parsed;
			}
			string updated = "";
			if (root.TryGetProperty("updatedUtc", out var stamp) && stamp.ValueKind == JsonValueKind.String)
			{
				updated = stamp.GetString()?.Trim() ?? "";
			}
			bool online = up.GetBoolean();
			string when = string.IsNullOrWhiteSpace(updated) ? source : source + "  " + updated;
			if (!online)
			{
				return new HomeStatus
				{
					ShowOnline = false,
					Headline = "Offline",
					PlayersText = "—",
					Detail = "Server is down.",
					Source = when
				};
			}
			return new HomeStatus
			{
				ShowOnline = true,
				Headline = "Online",
				PlayersText = players + " online",
				Detail = "Server is up.",
				Source = when
			};
		}
		catch (JsonException)
		{
			return Unknown("Server status file could not be read.");
		}
	}

	public static HomeStatus ReadFile(string path)
	{
		return FromFile(path);
	}

	private static HomeStatus FromFile(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return Unknown("Server status is not published yet.");
			}
			return Parse(File.ReadAllText(path), path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Unknown("Server status file could not be read.");
		}
	}

	private static HomeStatus FromUrl(string url)
	{
		try
		{
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
			string body = http.GetStringAsync(url).GetAwaiter().GetResult();
			return Parse(body, url);
		}
		catch (Exception)
		{
			return Unknown("Server status is not published yet.");
		}
	}

	private static HomeStatus Unknown(string detail)
	{
		return new HomeStatus
		{
			ShowOnline = false,
			Headline = "Unknown",
			PlayersText = "—",
			Detail = detail,
			Source = "Stub reads {up, players, updatedUtc} from " + PathVariable + ", " + UrlVariable + ", or " + HostStatusPath + "."
		};
	}
}
