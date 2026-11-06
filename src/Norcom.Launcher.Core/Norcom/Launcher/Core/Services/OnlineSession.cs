using System;
using System.Linq;

namespace Norcom.Launcher.Core.Services;

public static class OnlineSession
{
	public static MinecraftSession Create(string name, string uuid, string accessToken, string xuid)
	{
		return new MinecraftSession(name.Trim(), AccountStore.FormatUuid(uuid), accessToken, FromLauncher: false, "msa", xuid ?? "");
	}

	public static bool IsPlayable(MinecraftSession? session)
	{
		if ((object)session == null || session.FromLauncher)
		{
			return false;
		}
		if (!string.Equals(session.UserType, "msa", StringComparison.Ordinal))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(session.Name) || session.Name.Length > 16)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken == "0" || session.AccessToken.Length < 20)
		{
			return false;
		}
		if (session.AccessToken.Any(char.IsWhiteSpace))
		{
			return false;
		}
		return IsDashedUuid(session.Uuid);
	}

	public static bool IsDashedUuid(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length != 36)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i++)
		{
			bool flag;
			switch (i)
			{
			case 8:
			case 13:
			case 18:
			case 23:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				if (value[i] != '-')
				{
					return false;
				}
			}
			else if (!Uri.IsHexDigit(value[i]))
			{
				return false;
			}
		}
		return true;
	}
}
