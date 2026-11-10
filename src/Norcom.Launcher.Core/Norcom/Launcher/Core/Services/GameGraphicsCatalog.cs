using System;
using System.Collections.Generic;
using System.Globalization;

namespace Norcom.Launcher.Core.Services;

public sealed record PresetFloor(string Preset, long MinVramBytes, long MinRamBytes, int MinCores, int MinMhz);

public sealed class GraphicsSetting
{
	public string Key { get; init; } = "";

	public string Label { get; init; } = "";

	public string Help { get; init; } = "";

	public string Kind { get; init; } = "slider";

	public double Min { get; init; }

	public double Max { get; init; } = 1;

	public double Step { get; init; } = 1;

	public string[] Choices { get; init; } = Array.Empty<string>();

	public string Target { get; init; } = "options";

	public IReadOnlyDictionary<string, string> ByPreset { get; init; } = new Dictionary<string, string>();
}

public sealed class GameDefinition
{
	public string Id { get; init; } = "";

	public string Name { get; init; } = "";

	public string[] Presets { get; init; } = Array.Empty<string>();

	public PresetFloor[] Floors { get; init; } = Array.Empty<PresetFloor>();

	public GraphicsSetting[] Settings { get; init; } = Array.Empty<GraphicsSetting>();
}

public sealed class PresetChoice
{
	public string Preset { get; init; } = "Low";

	public string Reason { get; init; } = "";
}

public static class GameGraphicsCatalog
{
	public const long Gib = 1073741824L;

	public static IReadOnlyList<GameDefinition> Games { get; } = new GameDefinition[2] { Minecraft(), Zomboid() };

	public static GameDefinition Require(string id)
	{
		for (int i = 0; i < Games.Count; i++)
		{
			if (string.Equals(Games[i].Id, id, StringComparison.OrdinalIgnoreCase))
			{
				return Games[i];
			}
		}
		throw new InvalidOperationException("Unknown game profile: " + id);
	}

	public static PresetChoice Recommend(GameDefinition game, MachineSpecs specs)
	{
		string summary = Describe(specs);
		if (specs == null || !specs.HasGraphicsMemory)
		{
			string gap = specs != null && specs.Gaps.Count > 0 ? specs.Gaps[0] : "This PC could not be read.";
			return new PresetChoice
			{
				Preset = "Low",
				Reason = gap + " Low is the safe preset for " + game.Name + ". " + summary
			};
		}
		for (int i = 0; i < game.Floors.Length; i++)
		{
			PresetFloor floor = game.Floors[i];
			if (Meets(specs, floor))
			{
				return new PresetChoice
				{
					Preset = floor.Preset,
					Reason = game.Name + " " + floor.Preset + " wants " + FloorText(floor) + ". This PC has " + summary
				};
			}
		}
		return new PresetChoice
		{
			Preset = "Low",
			Reason = "This PC is below the other " + game.Name + " presets. " + summary
		};
	}

	public static Dictionary<string, string> PresetValues(GameDefinition game, string preset)
	{
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < game.Settings.Length; i++)
		{
			GraphicsSetting setting = game.Settings[i];
			if (setting.ByPreset.TryGetValue(preset, out string value))
			{
				values[setting.Key] = value;
			}
		}
		return values;
	}

	public static Dictionary<string, string> EffectiveValues(GameDefinition game, SavedGraphicsProfile profile)
	{
		Dictionary<string, string> values = PresetValues(game, profile.Preset);
		if (!profile.Auto && profile.Custom != null)
		{
			foreach (KeyValuePair<string, string> pair in profile.Custom)
			{
				values[pair.Key] = pair.Value;
			}
		}
		return values;
	}

	private static bool Meets(MachineSpecs specs, PresetFloor floor)
	{
		if (specs.VramBytes < floor.MinVramBytes || specs.RamBytes < floor.MinRamBytes || specs.LogicalCores < floor.MinCores)
		{
			return false;
		}
		if (floor.MinMhz > 0 && specs.CpuMhz > 0 && specs.CpuMhz < floor.MinMhz)
		{
			return false;
		}
		return true;
	}

	private static string FloorText(PresetFloor floor)
	{
		return Gb(floor.MinVramBytes) + " GB video memory, " + Gb(floor.MinRamBytes) + " GB memory, and " + floor.MinCores + " cores";
	}

	public static string Describe(MachineSpecs specs)
	{
		if (specs == null)
		{
			return "no spec reading.";
		}
		string gpu = string.IsNullOrWhiteSpace(specs.GpuName) ? "unknown graphics" : specs.GpuName.Trim();
		string display = specs.DisplayWidth > 0 ? specs.DisplayWidth + "x" + specs.DisplayHeight : "unknown display";
		string refresh = specs.RefreshHz > 0 ? specs.RefreshHz + " Hz" : "unknown refresh";
		string clock = specs.CpuMhz > 0 ? specs.CpuMhz + " MHz" : "unknown clock";
		return gpu + ", " + Gb(specs.VramBytes) + " GB video memory, " + Gb(specs.RamBytes) + " GB memory, " + specs.LogicalCores + " cores at " + clock + ", " + display + " " + refresh + ".";
	}

	private static string Gb(long bytes)
	{
		if (bytes <= 0)
		{
			return "0";
		}
		return (bytes / (double)Gib).ToString("0.#", CultureInfo.InvariantCulture);
	}

	private static GameDefinition Minecraft()
	{
		return new GameDefinition
		{
			Id = "minecraft",
			Name = "Minecraft",
			Presets = new string[4] { "Low", "Medium", "Medium-High", "Ultra" },
			Floors = SharedFloors(),
			Settings = new GraphicsSetting[11]
			{
				Slider("renderDistance", "Render distance", "How many chunks Minecraft draws around you.", "options", 2, 32, 1, "8", "12", "16", "24"),
				Slider("simulationDistance", "Simulation distance", "How far mobs, crops, and redstone stay active.", "options", 2, 32, 1, "6", "8", "10", "12"),
				Choice("graphicsMode", "Graphics mode", "Fast, Fancy, or Fabulous. Fabulous is the heaviest.", "options", new string[3] { "0", "1", "2" }, "0", "1", "2", "2"),
				Choice("particles", "Particles", "0 is all, 1 is decreased, 2 is minimal.", "options", new string[3] { "0", "1", "2" }, "2", "1", "1", "0"),
				Slider("entityDistanceScaling", "Entity distance", "How far entities are drawn. 1 is the usual distance.", "options", 0.25, 2, 0.25, "0.5", "0.75", "1", "1.25"),
				Slider("maxFps", "Max FPS", "Caps the frame rate. 260 leaves Minecraft uncapped for this pack.", "options", 30, 260, 10, "60", "120", "144", "260"),
				Toggle("enableVsync", "VSync", "Locks frames to the display. Off lets the FPS cap do the work.", "options", "false", "false", "false", "false"),
				Slider("mipmapLevels", "Mipmaps", "Sharper distant textures at the cost of memory.", "options", 0, 4, 1, "2", "4", "4", "4"),
				Slider("biomeBlendRadius", "Biome blend", "How smoothly grass and water colors blend. Higher is heavier.", "options", 0, 7, 1, "1", "2", "3", "5"),
				Slider("performance.chunk_builder_threads", "Sodium chunk threads", "0 lets Sodium choose. Written only when Sodium is installed.", "sodium", 0, 16, 1, "1", "2", "0", "0"),
				Slider("maxShadowRenderDistance", "Iris shadow distance", "Shadow chunks. Written only when Iris is installed.", "iris", 4, 32, 1, "8", "12", "16", "32")
			}
		};
	}

	private static GameDefinition Zomboid()
	{
		return new GameDefinition
		{
			Id = "zomboid",
			Name = "Project Zomboid",
			Presets = new string[4] { "Low", "Medium", "Medium-High", "Ultra" },
			Floors = SharedFloors(),
			Settings = new GraphicsSetting[7]
			{
				Slider("width", "Width", "Window or fullscreen width in pixels.", "zomboid", 800, 3840, 1, "1280", "1920", "1920", "2560"),
				Slider("height", "Height", "Window or fullscreen height in pixels.", "zomboid", 600, 2160, 1, "720", "1080", "1080", "1440"),
				Toggle("fullscreen", "Fullscreen", "Fills the display.", "zomboid", "false", "false", "true", "true"),
				Toggle("vsync", "VSync", "Locks frames to the display refresh.", "zomboid", "true", "true", "false", "false"),
				Slider("frameRate", "FPS cap", "Highest frame rate Zomboid will chase.", "zomboid", 30, 240, 1, "30", "60", "120", "144"),
				Slider("maxTextureSize", "Texture quality", "Higher uses more video memory.", "zomboid", 1, 4, 1, "1", "2", "3", "4"),
				Slider("lighting", "Lighting", "0 is simpler lighting. 3 is the heaviest.", "zomboid", 0, 3, 1, "0", "1", "2", "3")
			}
		};
	}

	private static PresetFloor[] SharedFloors()
	{
		return new PresetFloor[3]
		{
			new PresetFloor("Ultra", 12L * Gib, 16L * Gib, 8, 0),
			new PresetFloor("Medium-High", 7L * Gib, 16L * Gib, 6, 0),
			new PresetFloor("Medium", 4L * Gib, 8L * Gib, 4, 0)
		};
	}

	private static GraphicsSetting Slider(string key, string label, string help, string target, double min, double max, double step, string low, string medium, string mediumHigh, string ultra)
	{
		return new GraphicsSetting
		{
			Key = key,
			Label = label,
			Help = help,
			Kind = "slider",
			Min = min,
			Max = max,
			Step = step,
			Target = target,
			ByPreset = Map(low, medium, mediumHigh, ultra)
		};
	}

	private static GraphicsSetting Toggle(string key, string label, string help, string target, string low, string medium, string mediumHigh, string ultra)
	{
		return new GraphicsSetting
		{
			Key = key,
			Label = label,
			Help = help,
			Kind = "toggle",
			Target = target,
			Choices = new string[2] { "false", "true" },
			ByPreset = Map(low, medium, mediumHigh, ultra)
		};
	}

	private static GraphicsSetting Choice(string key, string label, string help, string target, string[] choices, string low, string medium, string mediumHigh, string ultra)
	{
		return new GraphicsSetting
		{
			Key = key,
			Label = label,
			Help = help,
			Kind = "choice",
			Target = target,
			Choices = choices,
			ByPreset = Map(low, medium, mediumHigh, ultra)
		};
	}

	private static Dictionary<string, string> Map(string low, string medium, string mediumHigh, string ultra)
	{
		return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Low"] = low,
			["Medium"] = medium,
			["Medium-High"] = mediumHigh,
			["Ultra"] = ultra
		};
	}
}
