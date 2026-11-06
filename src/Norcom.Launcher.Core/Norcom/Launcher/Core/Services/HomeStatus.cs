namespace Norcom.Launcher.Core.Services;

public sealed class HomeStatus
{
	public bool ShowOnline { get; init; }

	public string Headline { get; init; } = "Unknown";

	public string PlayersText { get; init; } = "—";

	public string Detail { get; init; } = "";

	public string Source { get; init; } = "";
}
