using System;
using System.Collections.Generic;
using System.Linq;

namespace Norcom.Launcher.Core.Services;

public static class VersionCompare
{
	public static int Compare(string? a, string? b)
	{
		Version version = ToVersion(a);
		Version value = ToVersion(b);
		return version.CompareTo(value);
	}

	public static Version ToVersion(string? s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return new Version(0, 0, 0);
		}
		string text = new string(s.Trim().TakeWhile((char c) => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
		if (text.Length == 0)
		{
			return new Version(0, 0, 0);
		}
		List<string> list = text.Split('.', StringSplitOptions.RemoveEmptyEntries).Take(4).ToList();
		while (list.Count < 3)
		{
			list.Add("0");
		}
		if (!Version.TryParse(string.Join('.', list), out Version result))
		{
			return new Version(0, 0, 0);
		}
		return result;
	}
}
