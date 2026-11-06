using System;
using System.Collections.Generic;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class LaunchLog
{
	public static string SafeArguments(IReadOnlyList<string> args)
	{
		string[] array = new string[args.Count];
		for (int i = 0; i < args.Count; i++)
		{
			bool flag = i > 0 && IsSecretFlag(args[i - 1]);
			array[i] = (flag ? "<redacted>" : Redactor.Clean(args[i]));
		}
		return string.Join(' ', array);
	}

	private static bool IsSecretFlag(string value)
	{
		if (!value.Equals("--accessToken", StringComparison.OrdinalIgnoreCase))
		{
			return value.Equals("--auth_access_token", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
