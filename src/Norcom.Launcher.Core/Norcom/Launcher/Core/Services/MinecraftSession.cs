namespace Norcom.Launcher.Core.Services;

public sealed record MinecraftSession(string Name, string Uuid, string AccessToken, bool FromLauncher, string UserType = "legacy", string Xuid = "")
{
	public override string ToString()
	{
		return UserType + " " + Name + " " + Uuid;
	}
}
