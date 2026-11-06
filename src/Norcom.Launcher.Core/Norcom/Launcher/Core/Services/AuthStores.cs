using System;
using System.Runtime.Versioning;

namespace Norcom.Launcher.Core.Services;

public static class AuthStores
{
	[SupportedOSPlatform("windows")]
	public static FileAuthStore Create(string directory)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("NORCOM account tokens are stored with Windows DPAPI.");
		}
		return new FileAuthStore(directory, new DpapiDataProtector());
	}
}
