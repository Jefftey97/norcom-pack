namespace Norcom.Launcher.Core.Services;

public static class PrismMessages
{
	public const string Intro = "Microsoft sign-in is handled by Prism in its normal folder. Play starts the NORCOM instance and joins the NORCOM server. Sign in once in that Prism window if it asks. Prism may show a window. Prism Launcher is a separate GPL program, not part of NORCOM. NORCOM does not read Prism's accounts.";

	public const string Missing = "Prism Launcher is not installed. Install it from the official Prism site, then press Play. Sign in with Microsoft in Prism's window if it asks. NORCOM does not start Minecraft without that.";

	public const string Opening = "Opening Prism for the NORCOM instance. If Prism asks, sign in with Microsoft in that window. NORCOM does not keep that login and did not start an offline game.";

	public const string Starting = "Starting…";

	public const string Closed = "Prism closed. If you finished its Microsoft sign-in, it started the NORCOM instance. Later Play uses the same Prism. NORCOM does not keep that login.";

	public const string ClosedWithoutGame = "Prism closed without Minecraft. Finish Microsoft sign-in in Prism if it asked, then press Play again.";

	public const string NotDetected = "Prism stayed open, but Minecraft was not detected. If the game window is open, you are playing. Otherwise close Prism and press Play again.";

	public const string Rejected = "Prism did not start Minecraft. Finish Microsoft sign-in in Prism's window if it asked, then press Play again. NORCOM did not use a local name.";

	public const string LinkBlocked = "NORCOM's Prism instance already has a minecraft folder that is not the NORCOM game folder. It was not replaced, and Minecraft was not started.";

	public const string LinkKept = "NORCOM could not attach this Prism instance to the NORCOM game folder. Saves were not deleted.";

	public const string Claimed = "Claimed the existing Prism NORCOM instance. Saves were kept.";
}
