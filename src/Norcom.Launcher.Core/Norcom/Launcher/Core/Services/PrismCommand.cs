using System;
using System.Collections.Generic;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public static class PrismCommand
{
	public const string InstanceId = "NORCOM";

	public static string DefaultDataDir(string appData)
	{
		return Path.Combine(appData, "PrismLauncher");
	}

	public static IReadOnlyList<string> Build(string serverHost, int serverPort)
	{
		bool flag = string.IsNullOrWhiteSpace(serverHost);
		if (!flag)
		{
			bool flag2 = ((serverPort < 1 || serverPort > 65535) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new InvalidOperationException("NORCOM server address is missing.");
		}
		return new string[4]
		{
			"--launch",
			"NORCOM",
			"--server",
			serverHost.Trim() + ":" + serverPort
		};
	}
}
