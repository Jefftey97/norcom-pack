using System;
using System.Collections.Generic;
using System.Globalization;
using Norcom.Launcher.Core.Games;

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

	/// <summary>Display names for <see cref="Choices"/>, same order. Empty means show the raw values.</summary>
	public string[] ChoiceLabels { get; init; } = Array.Empty<string>();

	public string ChoiceLabel(int index)
	{
		return index >= 0 && index < ChoiceLabels.Length && !string.IsNullOrWhiteSpace(ChoiceLabels[index]) ? ChoiceLabels[index] : Choices[index];
	}

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

	private static readonly List<GameDefinition> Registered = new List<GameDefinition>();

	public static IReadOnlyList<GameDefinition> Games
	{
		get
		{
			GameHost.Ensure();
			return Registered;
		}
	}

	public static void Register(GameDefinition definition)
	{
		if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
		{
			return;
		}
		for (int i = 0; i < Registered.Count; i++)
		{
			if (string.Equals(Registered[i].Id, definition.Id, StringComparison.OrdinalIgnoreCase))
			{
				Registered[i] = definition;
				return;
			}
		}
		Registered.Add(definition);
	}

	public static GameDefinition Require(string id)
	{
		GameHost.Ensure();
		for (int i = 0; i < Registered.Count; i++)
		{
			if (string.Equals(Registered[i].Id, id, StringComparison.OrdinalIgnoreCase))
			{
				return Registered[i];
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
		int threads = specs.LogicalCores;
		int cores = specs.PhysicalCores > 0 ? specs.PhysicalCores : threads;
		string cpu = threads > 0 ? "Cores: " + cores.ToString(CultureInfo.InvariantCulture) + " cores, " + threads.ToString(CultureInfo.InvariantCulture) + " threads" : "Cores: not detected on this PC";
		return gpu + ", " + Gb(specs.VramBytes) + " GB video memory, " + Gb(specs.RamBytes) + " GB memory, " + cpu + " at " + clock + ", " + display + " " + refresh + ".";
	}

	private static string Gb(long bytes)
	{
		if (bytes <= 0)
		{
			return "0";
		}
		return (bytes / (double)Gib).ToString("0.#", CultureInfo.InvariantCulture);
	}

	internal static PresetFloor[] SharedFloors()
	{
		return new PresetFloor[3]
		{
			new PresetFloor("Ultra", 12L * Gib, 16L * Gib, 8, 0),
			new PresetFloor("Medium-High", 7L * Gib, 16L * Gib, 6, 0),
			new PresetFloor("Medium", 4L * Gib, 8L * Gib, 4, 0)
		};
	}

	internal static GraphicsSetting Slider(string key, string label, string help, string target, double min, double max, double step, string low, string medium, string mediumHigh, string ultra)
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

	internal static GraphicsSetting Toggle(string key, string label, string help, string target, string low, string medium, string mediumHigh, string ultra)
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

	internal static GraphicsSetting Choice(string key, string label, string help, string target, string[] choices, string low, string medium, string mediumHigh, string ultra, string[] choiceLabels = null)
	{
		return new GraphicsSetting
		{
			Key = key,
			Label = label,
			Help = help,
			Kind = "choice",
			Target = target,
			Choices = choices,
			ChoiceLabels = choiceLabels ?? Array.Empty<string>(),
			ByPreset = Map(low, medium, mediumHigh, ultra)
		};
	}

	internal static Dictionary<string, string> Map(string low, string medium, string mediumHigh, string ultra)
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
