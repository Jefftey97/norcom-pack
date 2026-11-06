namespace Norcom.Launcher.Core.Services;

public static class MicrosoftSignInRoute
{
	public const string Audience = "PersonalMicrosoftAccount";

	public const string Authority = "https://login.microsoftonline.com/consumers";

	public const string SystemBrowserRedirect = "http://localhost";

	public const string InstalledAppRedirect = "https://login.microsoftonline.com/common/oauth2/nativeclient";

	public static readonly string[] Scopes = new string[2] { "XboxLive.signin", "offline_access" };
}
