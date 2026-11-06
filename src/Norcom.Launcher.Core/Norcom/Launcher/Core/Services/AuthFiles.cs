using System.IO;
using System.Linq;

namespace Norcom.Launcher.Core.Services;

public static class AuthFiles
{
	public const string Session = "session.bin";

	public const string Msal = "msal.bin";

	public const string ClientId = "client-id.txt";

	public const string BoundClient = "bound-client.txt";

	public static string? ReadClientId(string authDirectory)
	{
		string text = Path.Combine(authDirectory, "client-id.txt");
		if (!File.Exists(text))
		{
			return null;
		}
		if (new FileInfo(text).Length > 200)
		{
			return null;
		}
		string text2 = File.ReadLines(text).FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2.Trim();
		}
		return null;
	}
}
