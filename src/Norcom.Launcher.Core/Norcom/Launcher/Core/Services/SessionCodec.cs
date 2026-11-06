using System;
using System.Text;
using System.Text.Json;

namespace Norcom.Launcher.Core.Services;

internal static class SessionCodec
{
	public static byte[] Write(MinecraftSession session, DateTimeOffset expires)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		stringBuilder.Append("{\"name\":");
		stringBuilder.Append(Json(session.Name));
		stringBuilder.Append(",\"uuid\":");
		stringBuilder.Append(Json(session.Uuid));
		stringBuilder.Append(",\"xuid\":");
		stringBuilder.Append(Json(session.Xuid));
		stringBuilder.Append(",\"token\":");
		stringBuilder.Append(Json(session.AccessToken));
		stringBuilder.Append(",\"expires\":");
		stringBuilder.Append(expires.ToUnixTimeSeconds());
		stringBuilder.Append(",\"userType\":\"msa\"}");
		return Encoding.UTF8.GetBytes(stringBuilder.ToString());
	}

	public static bool TryRead(byte[] plaintext, out MinecraftSession session, out DateTimeOffset expires)
	{
		session = null;
		expires = default(DateTimeOffset);
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(plaintext);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.GetProperty("userType").GetString() != "msa")
			{
				return false;
			}
			string name = rootElement.GetProperty("name").GetString() ?? "";
			string uuid = rootElement.GetProperty("uuid").GetString() ?? "";
			string xuid = rootElement.GetProperty("xuid").GetString() ?? "";
			string accessToken = rootElement.GetProperty("token").GetString() ?? "";
			expires = DateTimeOffset.FromUnixTimeSeconds(rootElement.GetProperty("expires").GetInt64());
			session = OnlineSession.Create(name, uuid, accessToken, xuid);
			return OnlineSession.IsPlayable(session);
		}
		catch
		{
			return false;
		}
	}

	private static string Json(string value)
	{
		return JsonSerializer.Serialize(value);
	}
}
