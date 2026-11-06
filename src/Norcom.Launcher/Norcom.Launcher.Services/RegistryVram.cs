using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Norcom.Launcher.Services;

internal static class RegistryVram
{
	public static IReadOnlyList<(string Name, long Bytes)> Read()
	{
		List<(string, long)> list = new List<(string, long)>();
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}");
			if (registryKey == null)
			{
				return list;
			}
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string text in subKeyNames)
			{
				if (text.Length != 4 || !char.IsDigit(text[0]))
				{
					continue;
				}
				using RegistryKey registryKey2 = registryKey.OpenSubKey(text);
				string text2 = registryKey2?.GetValue("DriverDesc") as string;
				long num = Qword(registryKey2?.GetValue("HardwareInformation.qwMemorySize"));
				if (!string.IsNullOrWhiteSpace(text2) && num > 0)
				{
					list.Add((text2, num));
				}
			}
			return list;
		}
		catch
		{
			return list;
		}
	}

	private static long Qword(object? value)
	{
		if (!(value is long result))
		{
			if (!(value is ulong num))
			{
				if (value is byte[] array && array.Length >= 8)
				{
					return BitConverter.ToInt64(array, 0);
				}
				return 0L;
			}
			return (long)((num > long.MaxValue) ? long.MaxValue : num);
		}
		return result;
	}
}
