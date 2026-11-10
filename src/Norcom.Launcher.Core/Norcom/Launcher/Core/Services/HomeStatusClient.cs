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
				Headline = "Unknown",
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
		return null;
	}

	public static HomeStatus Parse(string json, string source, DateTimeOffset? now=null)
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
        var clock=now??DateTimeOffset.UtcNow;
        if(!DateTimeOffset.TryParse(updated,out var sampled) || clock-sampled>TimeSpan.FromMinutes(2) || sampled-clock>TimeSpan.FromMinutes(1))
            return new HomeStatus{ShowOnline=false,Headline="Unknown",PlayersText="—",Detail="The status snapshot is stale or has no valid timestamp. Refresh to try again.",Source=source};
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

	public static HomeStatus? ReadFile(string path, DateTimeOffset? now=null)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return null;
			}
			if(new FileInfo(path).Length>32768)return null;
            return Parse(File.ReadAllText(path), path,now);
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
			using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2),MaxResponseContentBufferSize=32768 };
			string body = http.GetStringAsync(url).GetAwaiter().GetResult();
			return Parse(body, url);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
