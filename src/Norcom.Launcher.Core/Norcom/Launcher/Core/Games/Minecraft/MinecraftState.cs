using System.IO;
using System.Text.Json;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Games.Minecraft;

public sealed class MinecraftState
{
	public int RamMb { get; set; } = 8192;

	public bool RamSpecified { get; set; }

	public bool RamPending { get; set; }

	public string TexturePack { get; set; } = "";

	public bool TexturePackSpecified { get; set; }

	public bool TexturePackPending { get; set; }

	public static string PathFor(string home)
	{
		return Path.Combine(home, "minecraft-state.json");
	}

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public static MinecraftState Load(string home)
	{
		string path = PathFor(home);
		if (File.Exists(path))
		{
			return JsonSerializer.Deserialize<MinecraftState>(File.ReadAllText(path), Json) ?? new MinecraftState();
		}
		LauncherSettings legacy = LauncherSettings.Load(Path.Combine(home, "settings.json"));
		MinecraftState state = new MinecraftState
		{
			RamMb = legacy.RamMb,
			RamSpecified = legacy.RamSpecified,
			RamPending = legacy.RamPending,
			TexturePack = legacy.TexturePack ?? "",
			TexturePackSpecified = legacy.TexturePackSpecified,
			TexturePackPending = legacy.TexturePackPending
		};
		state.Save(home);
		return state;
	}

	public void Save(string home)
	{
		Directory.CreateDirectory(home);
		File.WriteAllText(PathFor(home), JsonSerializer.Serialize(this, Json));
	}
}
