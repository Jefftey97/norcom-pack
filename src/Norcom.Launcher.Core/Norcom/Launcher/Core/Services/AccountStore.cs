using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class AccountStore
{
	public static MinecraftSession Local(string name)
	{
		string name2 = name.Trim();
		return new MinecraftSession(name2, OfflineUuid(name2), "0", FromLauncher: false);
	}

	public static string OfflineUuid(string name)
	{
		byte[] array = MD5.HashData(EncodingUtf8("OfflinePlayer:" + name));
		array[6] = (byte)((array[6] & 0xFu) | 0x30u);
		array[8] = (byte)((array[8] & 0x3Fu) | 0x80u);
		return FormatUuid(Convert.ToHexString(array).ToLowerInvariant());
	}

	public static string FormatUuid(string hex)
	{
		string text = hex.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
		if (text.Length != 32)
		{
			return hex;
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 5);
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(0, 8));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(8, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(12, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		defaultInterpolatedStringHandler.AppendFormatted(text.Substring(16, 4));
		defaultInterpolatedStringHandler.AppendLiteral("-");
		string text2 = text;
		defaultInterpolatedStringHandler.AppendFormatted(text2.Substring(20, text2.Length - 20));
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}

	private static byte[] EncodingUtf8(string s)
	{
		return Encoding.UTF8.GetBytes(s);
	}
}
