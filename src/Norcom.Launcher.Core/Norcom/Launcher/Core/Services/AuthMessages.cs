namespace Norcom.Launcher.Core.Services;

public static class AuthMessages
{
	public const string NotConfigured = "NORCOM Microsoft sign-in is not configured. Add the NORCOM app id before signing in. Play was not started.";

	public const string InvalidClientId = "The NORCOM app id is not a valid public client id. Play was not started.";

	public const string BorrowedClientId = "That id belongs to the official Minecraft launcher. NORCOM cannot use it. Play was not started.";

	public const string Cancelled = "Sign-in was cancelled. Play was not started.";

	public const string Network = "Microsoft or Minecraft could not be reached. Play was not started.";

	public const string MojangNotApproved = "Mojang has not approved this NORCOM application. Sign-in cannot finish until that approval exists.";

	public const string NoEntitlement = "This account does not have Minecraft Java Edition. Play was not started.";

	public const string NoProfile = "This account has no Minecraft Java profile yet. Create one at minecraft.net, then sign in again.";

	public const string XboxNoAccount = "This Microsoft account has no Xbox profile. Sign in at xbox.com once, then try again.";

	public const string XboxUnavailable = "Xbox Live is not available for this account. Minecraft sign-in cannot continue.";

	public const string XboxAdult = "Xbox Live requires adult verification for this account before Minecraft can sign in.";

	public const string XboxChild = "This is a child Microsoft account. An adult has to add it to a Microsoft family before Xbox sign-in works.";

	public const string InteractionRequired = "The saved sign-in expired. Sign in again. Play was not started.";

	public const string Rejected = "Microsoft sign-in was refused. Play was not started.";

	public const string NeedSignIn = "Sign in with Microsoft in Settings. NORCOM does not read the official launcher account, and Play does not start without a verified Minecraft profile.";

	public const string NotStarted = "Sign in with Microsoft before Play. NORCOM did not start Minecraft.";

	public static string SignedIn(string name)
	{
		return "Signed in as " + name + ". Play uses this Minecraft Java profile.";
	}

	public static string Expired(string name)
	{
		return "Saved sign-in for " + name + " expired. Press Play to refresh it, or sign in again.";
	}

	public static string XboxCode(long code)
	{
		return "Xbox sign-in was refused (" + code + "). Play was not started.";
	}

	public static string For(AuthFailure failure)
	{
		return failure switch
		{
			AuthFailure.NotConfigured => "NORCOM Microsoft sign-in is not configured. Add the NORCOM app id before signing in. Play was not started.", 
			AuthFailure.InvalidClientId => "The NORCOM app id is not a valid public client id. Play was not started.", 
			AuthFailure.Cancelled => "Sign-in was cancelled. Play was not started.", 
			AuthFailure.Network => "Microsoft or Minecraft could not be reached. Play was not started.", 
			AuthFailure.MojangNotApproved => "Mojang has not approved this NORCOM application. Sign-in cannot finish until that approval exists.", 
			AuthFailure.NoEntitlement => "This account does not have Minecraft Java Edition. Play was not started.", 
			AuthFailure.NoProfile => "This account has no Minecraft Java profile yet. Create one at minecraft.net, then sign in again.", 
			AuthFailure.XboxNoAccount => "This Microsoft account has no Xbox profile. Sign in at xbox.com once, then try again.", 
			AuthFailure.XboxUnavailable => "Xbox Live is not available for this account. Minecraft sign-in cannot continue.", 
			AuthFailure.XboxAdult => "Xbox Live requires adult verification for this account before Minecraft can sign in.", 
			AuthFailure.XboxChild => "This is a child Microsoft account. An adult has to add it to a Microsoft family before Xbox sign-in works.", 
			AuthFailure.InteractionRequired => "The saved sign-in expired. Sign in again. Play was not started.", 
			_ => "Microsoft sign-in was refused. Play was not started.", 
		};
	}
}
