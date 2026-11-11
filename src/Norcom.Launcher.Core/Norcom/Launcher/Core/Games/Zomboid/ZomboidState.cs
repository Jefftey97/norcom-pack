using System.IO;
using System.Text.Json;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Games.Zomboid;

public sealed class ZomboidState
{
	public int RamMb { get; set; }

	public bool RamSpecified { get; set; }

	public bool RamPending { get; set; }

	public static string PathFor(string home)
	{
		return Path.Combine(home, "zomboid-state.json");
	}

	public static ZomboidState Load(string home)
	{
		string path = PathFor(home);
		if (File.Exists(path))
		{
			return JsonSerializer.Deserialize<ZomboidState>(File.ReadAllText(path)) ?? new ZomboidState();
		}
		LauncherSettings legacy = LauncherSettings.Load(Path.Combine(home, "settings.json"));
		ZomboidState state = new ZomboidState
		{
			RamMb = legacy.ZomboidRamMb,
			RamSpecified = legacy.ZomboidRamSpecified,
			RamPending = legacy.ZomboidRamPending
		};
		state.Save(home);
		return state;
	}

	public void Save(string home)
	{
		Directory.CreateDirectory(home);
		File.WriteAllText(PathFor(home), JsonSerializer.Serialize(this));
	}
}
