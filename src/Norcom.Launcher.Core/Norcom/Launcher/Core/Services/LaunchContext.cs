namespace Norcom.Launcher.Core.Services;

public sealed class LaunchContext
{
	public string OsName { get; init; } = "windows";


	public string Arch { get; init; } = "x64";


	public string GameDir { get; init; } = "";


	public string AssetsDir { get; init; } = "";


	public string NativesDir { get; init; } = "";


	public string Classpath { get; init; } = "";


	public string VersionName { get; init; } = "";


	public string VersionType { get; init; } = "release";


	public string PlayerName { get; init; } = "Friend";


	public string Uuid { get; init; } = "";


	public string AccessToken { get; init; } = "0";


	public string UserType { get; init; } = "legacy";


	public string Xuid { get; init; } = "";


	public int RamMb { get; init; } = 6144;


	public string LogConfigPath { get; init; } = "";


	public string AssetsIndex { get; init; } = "";

}
