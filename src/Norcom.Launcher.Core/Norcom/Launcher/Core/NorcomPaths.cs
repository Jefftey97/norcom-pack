using System;
using System.IO;

namespace Norcom.Launcher.Core;

public static class NorcomPaths
{
	public const string PackTomlUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string ManifestApi = "https://api.github.com/repos/Jefftey97/norcom-pack/contents/launcher/manifest.json?ref=main";

	public const string ManifestJsDelivr = "https://cdn.jsdelivr.net/gh/Jefftey97/norcom-pack@main/launcher/manifest.json";

	public const string ManifestRaw = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/refs/heads/main/launcher/manifest.json";

	public const string ServerHost = "shrimp-cub.tun.ply.gg";

	public const int ProtocolVersion = 770;

	public const string MinecraftVersion = "26.3";

	public const string FabricLoader = "0.20.1";

	public const string ShaderPackFile = "ComplementaryUnbound_r5.9.3.zip";

	public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

	public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

	public static string GameDir => Override("NORCOM_GAME_DIR") ?? Path.Combine(AppData, ".norcom");

	public static string NorcomHome => Override("NORCOM_STATE_DIR") ?? Path.Combine(LocalAppData, "NORCOM");

	public static string DesktopDirectory => Override("NORCOM_DESKTOP") ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

	public static bool DisposableProfile
	{
		get
		{
			if (Override("NORCOM_GAME_DIR") == null && Override("NORCOM_STATE_DIR") == null)
			{
				return Override("NORCOM_DESKTOP") != null;
			}
			return true;
		}
	}

	public static string RecoveryRoot => Path.Combine(NorcomHome, "recovery");

	public static string LauncherDir => Path.Combine(NorcomHome, "Launcher");

	public static string SettingsPath => Path.Combine(NorcomHome, "settings.json");

	public static string LogPath => Path.Combine(LauncherDir, "launcher.log");

	public static string ManagedManifestPath => Path.Combine(NorcomHome, "managed-files.json");

	public static string PackwizCacheDir => Path.Combine(NorcomHome, "cache", "packwiz");

	public static string SkinsDir => Path.Combine(GameDir, "norcom-skins");

	public static string ScreenshotsDir => Path.Combine(GameDir, "screenshots");

	public static string JreJava => Path.Combine(NorcomHome, "jre", "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

	public static string GameLockPath => Path.Combine(LauncherDir, "game.lock");

	public static string AuthDir => Path.Combine(NorcomHome, "auth");

	public static string MsaClientIdPath => Path.Combine(AuthDir, "client-id.txt");

	public static string CrashOutbox => Path.Combine(LauncherDir, "diagnostics-v3", "outbox");

	public static string FabricVersionId => "fabric-loader-0.20.1-26.3";

	public static string FabricProfileUrl => "https://meta.fabricmc.net/v2/versions/loader/26.3/0.20.1/profile/json";

	private static string? Override(string name)
	{
		string environmentVariable = Environment.GetEnvironmentVariable(name);
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable.Trim();
		}
		return null;
	}
}
