using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Norcom.Launcher.Core;

namespace Norcom.Launcher.Core.Services;

public static class HomeStatusClient
{
	public const string PathVariable = "NORCOM_STATUS_PATH";

	public const string UrlVariable = "NORCOM_STATUS_URL";

	public const string HostStatusPath = "G:\\MinecraftServer\\.norcom\\status.json";

	public static HomeStatus Read()
	{
		HomeStatus? over = TryOverride();
		if (over != null)
		{
			return over;
		}
		FriendsEndpoint endpoint = FriendsServer.Resolve();
		try
		{
			ServerStatus ping = ServerStatusClient.PingAsync(endpoint.Host, endpoint.Port, NorcomPaths.ProtocolVersion, CancellationToken.None).GetAwaiter().GetResult();
			return FromPing(ping, endpoint.Display);
		}
		catch (Exception)
		{
			return FromPing(new ServerStatus(Online: false, 0, 0, "", -1, Array.Empty<string>(), "The friends server did not answer."), endpoint.Display);
		}
	}

	public static HomeStatus FromPing(ServerStatus status)
	{
		return FromPing(status, FriendsServer.FallbackDisplay);
	}

	public static HomeStatus FromPing(ServerStatus status, string source)
	{
		string where = string.IsNullOrWhiteSpace(source) ? FriendsServer.FallbackDisplay : source.Trim();
		if (!status.Online)
		{
			string detail = string.IsNullOrWhiteSpace(status.Error) ? "The friends server did not answer." : status.Error.Trim();
			return new HomeStatus
			{
				ShowOnline = false,
				Headline = "Offline",
				PlayersText = "—",
				Detail = detail,
				Source = where
			};
		}
		return new HomeStatus
		{
			ShowOnline = true,
			Headline = "Online",
			PlayersText = status.Players + " online",
			Detail = "Server is up.",
			Source = where
		};
	}

	public static HomeStatus? TryOverride()
	{
		string? path = Environment.GetEnvironmentVariable(PathVariable);
		if (!string.IsNullOrWhiteSpace(path))
		{
			HomeStatus? fromEnv = ReadFile(path.Trim());
			if (fromEnv != null)
			{
				return fromEnv;
			}
		}
		string? url = Environment.GetEnvironmentVariable(UrlVariable);
		if (!string.IsNullOrWhiteSpace(url))
		{
			HomeStatus? fromUrl = FromUrl(url.Trim());
			if (fromUrl != null)
			{
				return fromUrl;
			}
		}
		if (File.Exists(HostStatusPath))
		{
			HomeStatus? host = ReadFile(HostStatusPath);
			if (host != null)
			{
				return host;
			}
		}
		string local = Path.Combine(NorcomPaths.LauncherDir, "status.json");
		if (File.Exists(local))
		{
			return ReadFile(local);
		}
		return null;
	}

	public static HomeStatus Parse(string json, string source)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("up", out var up) || (up.ValueKind != JsonValueKind.True && up.ValueKind != JsonValueKind.False))
		{
			throw new JsonException("status up");
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

	public static HomeStatus? ReadFile(string path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return null;
			}
			return Parse(File.ReadAllText(path), path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return null;
		}
	}

	private static HomeStatus? FromUrl(string url)
	{
		try
		{
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
			string body = http.GetStringAsync(url).GetAwaiter().GetResult();
			return Parse(body, url);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
