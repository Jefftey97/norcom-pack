namespace Norcom.Launcher.Core.Services;

public sealed class AuthSnapshot
{
	public bool SignedIn { get; init; }

	public string Message { get; init; } = "Sign in with Microsoft in Settings. NORCOM does not read the official launcher account, and Play does not start without a verified Minecraft profile.";


	public override string ToString()
	{
		return Message;
	}
}
