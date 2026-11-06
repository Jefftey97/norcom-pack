using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class Hashing
{
	public static async Task<string> FileHexAsync(string path, string algorithm, CancellationToken ct)
	{
		FileStream stream = File.OpenRead(path);
		string result;
		try
		{
			string text = algorithm.ToLowerInvariant();
			HashAlgorithm hashAlgorithm = ((text == "sha512") ? SHA512.Create() : ((!(text == "sha1")) ? ((HashAlgorithm)SHA256.Create()) : ((HashAlgorithm)SHA1.Create())));
			using HashAlgorithm hasher = hashAlgorithm;
			result = Convert.ToHexString(await hasher.ComputeHashAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false)).ToLowerInvariant();
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return result;
	}

	public static bool EqualsHex(string? actual, string? expected)
	{
		if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
		{
			return false;
		}
		return actual.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
