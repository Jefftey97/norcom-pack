using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Norcom.Launcher.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtector : IDataProtector
{
	private static readonly byte[] Entropy = "NORCOM.auth.v1"u8.ToArray();

	public byte[] Protect(byte[] plaintext)
	{
		return ProtectedData.Protect(plaintext, Entropy, (DataProtectionScope)0);
	}

	public byte[] Unprotect(byte[] ciphertext)
	{
		return ProtectedData.Unprotect(ciphertext, Entropy, (DataProtectionScope)0);
	}
}
