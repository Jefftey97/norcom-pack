namespace Norcom.Launcher.Core.Services;

public static class NavPaint
{
	public const string SelectedFill = "#157A96";

	public const string SelectedBorder = "#3DF0FF";

	public const string SelectedText = "#F4FCFF";

	public const string IdleFill = "#00000000";

	public const string IdleBorder = "#00000000";

	public const string IdleText = "#E8F6FF";

	public static (string Fill, string Border, string Text, bool Bold) For(bool selected)
	{
		if (!selected)
		{
			return (Fill: "#00000000", Border: "#00000000", Text: "#E8F6FF", Bold: false);
		}
		return (Fill: "#157A96", Border: "#3DF0FF", Text: "#F4FCFF", Bold: true);
	}
}
