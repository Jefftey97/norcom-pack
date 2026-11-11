using System;
using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public static class DistantHorizonsDefaults
{
	public static bool EnsureFresh(string gameDir, GraphicsTier tier)
	{
		_ = gameDir;
		_ = tier;
		return false;
	}

	public static void ApplyPreset(string gameDir, GraphicsTier tier)
	{
		Write(Path.Combine(gameDir, "config", "DistantHorizons.toml"), tier);
	}

	public static IReadOnlyDictionary<string, string> Keys(GraphicsTier tier)
	{
		bool scaled = tier == GraphicsTier.Low || tier == GraphicsTier.Medium;
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["client.advanced.graphics.texture.enableTexturedLods"] = "true",
			["client.advanced.graphics.texture.maxTexturedLodDetailLevel"] = scaled ? "2" : "4",
			["client.advanced.graphics.noiseTexture.enableNoiseTexture"] = "true",
			["client.advanced.graphics.noiseTexture.noiseIntensity"] = "0.1",
			["client.advanced.graphics.quality.maxHorizontalResolution"] = "BLOCK",
			["client.advanced.graphics.quality.verticalQuality"] = scaled ? "MEDIUM" : "HIGH",
			["client.advanced.graphics.quality.horizontalQuality"] = "HIGH",
			["client.advanced.graphics.ssao.enableSsao"] = scaled ? "false" : "true"
		};
	}

	private static void Write(string path, GraphicsTier tier)
	{
		string directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string existing = File.Exists(path) ? File.ReadAllText(path) : "";
		if (!TomlValuePatch.TryApply(existing, Keys(tier), out string updated, out string error))
		{
			throw new InvalidDataException(error);
		}
		updated = updated.Replace("noiseIntensity = 0.1", "noiseIntensity = \"0.1\"", StringComparison.Ordinal);
		if (!string.Equals(existing, updated, StringComparison.Ordinal))
		{
			File.WriteAllText(path, updated);
		}
	}
}
