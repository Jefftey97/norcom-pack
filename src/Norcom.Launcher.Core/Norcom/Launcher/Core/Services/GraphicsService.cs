using System;
using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class GraphicsService
{
	private static readonly string[] CacheDirs = new string[8] { "shaderpacks/shadercache", "shader_cache", "cache/iris", "iris-cache", ".iris-cache", "shaderpacks/.iris", "config/iris-shadercache", "config/iris-reserved/cache" };

	public GraphicsPreset Apply(string gameDir, GraphicsTier tier)
	{
		GraphicsPreset graphicsPreset = GraphicsPresets.For(tier);
		Directory.CreateDirectory(gameDir);
		Directory.CreateDirectory(Path.Combine(gameDir, "config"));
		Directory.CreateDirectory(Path.Combine(gameDir, "shaderpacks"));
		string path = Path.Combine(gameDir, "options.txt");
		string existing = (File.Exists(path) ? File.ReadAllText(path) : null);
		bool fabulous = graphicsPreset.Tier is GraphicsTier.MediumHigh or GraphicsTier.High or GraphicsTier.Ultra;
		Dictionary<string, string> updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["renderDistance"] = graphicsPreset.RenderDistance.ToString(),
			["simulationDistance"] = graphicsPreset.SimulationDistance.ToString(),
			["maxFps"] = graphicsPreset.MaxFps.ToString(),
			["entityDistanceScaling"] = graphicsPreset.EntityDistance,
			["graphicsMode"] = fabulous ? "2" : "1",
			["biomeBlendRadius"] = fabulous ? "7" : "2",
			["particles"] = graphicsPreset.Particles.ToString(),
			["renderClouds"] = ((graphicsPreset.Tier == GraphicsTier.Low) ? "false" : "true"),
			["enableVsync"] = "false",
			["resourcePacks"] = "[\"vanilla\",\"continuity:default\",\"continuity:glass_pane_culling_fix\",\"file/Patrix_26.3_32x_basic.zip\",\"file/Patrix_26.3_32x_addon.zip\",\"file/Patrix_26.3_models.zip\"]"
		};
		File.WriteAllText(path, OptionsPatch.Apply(existing, updates));
		string path2 = Path.Combine(gameDir, "config", "iris.properties");
		string existing2 = (File.Exists(path2) ? File.ReadAllText(path2) : null);
		Dictionary<string, string> updates2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["enableShaders"] = "true",
			["shaderPack"] = "ComplementaryUnbound_r5.9.3.zip",
			["maxShadowRenderDistance"] = graphicsPreset.IrisShadowChunks.ToString(),
			["colorSpace"] = "SRGB",
			["allowUnknownShaders"] = "false"
		};
		File.WriteAllText(path2, OptionsPatch.ApplyProperties(existing2, updates2));
		string contents = GraphicsPresets.ShaderOptions(graphicsPreset);
		File.WriteAllText(Path.Combine(gameDir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), contents);
		File.WriteAllText(Path.Combine(gameDir, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip.txt"), contents);
		WriteDistantHorizons(Path.Combine(gameDir, "config", "DistantHorizons.toml"), graphicsPreset);
		SeedSodiumIfMissing(Path.Combine(gameDir, "config", "sodium-options.json"));
		string tierNote = "tier=" + tier.ToString() + "\nappliedUtc=" + DateTimeOffset.UtcNow.ToString("o") + "\n";
		File.WriteAllText(Path.Combine(gameDir, "config", "norcom-tier.txt"), tierNote);
		File.WriteAllText(Path.Combine(gameDir, "norcom-graphics-tier.txt"), tierNote);
		LauncherLog.Write("Graphics applied " + GraphicsPresets.TierLabel(tier));
		return graphicsPreset;
	}

	public string BackupAndReset(string gameDir, GraphicsTier tier)
	{
		string text = DateTime.Now.ToString("yyyyMMdd-HHmmss");
		string text2 = Path.Combine(gameDir, "norcom-backups", "visuals-" + text);
		Directory.CreateDirectory(text2);
		string[] array = new string[6] { "options.txt", "config/iris.properties", "config/sodium-options.json", "config/DistantHorizons.toml", "shaderpacks/ComplementaryUnbound_r5.9.3.txt", "shaderpacks/ComplementaryUnbound_r5.9.3.zip.txt" };
		foreach (string text3 in array)
		{
			string text4 = Path.Combine(gameDir, text3.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(text4))
			{
				string text5 = Path.Combine(text2, text3.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(text5));
				File.Copy(text4, text5, overwrite: true);
			}
		}
		Apply(gameDir, tier);
		return text2;
	}

	public IReadOnlyList<string> ClearShaderCache(string gameDir)
	{
		List<string> list = new List<string>();
		string[] cacheDirs = CacheDirs;
		foreach (string text in cacheDirs)
		{
			string path = Path.Combine(gameDir, text.Replace('/', Path.DirectorySeparatorChar));
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
					list.Add(text);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("Shader cache clear failed " + text + ": " + ex.Message);
				}
			}
		}
		return list;
	}

	private static void WriteDistantHorizons(string path, GraphicsPreset preset)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		if (!File.Exists(path))
		{
			string contents = "[client]\n [client.advanced]\n [client.advanced.graphics]\n [client.advanced.graphics.quality]\n lodChunkRenderDistanceRadius = RADIUS\n horizontalQuality = \"QUALITY\"\n verticalQuality = \"QUALITY\"\n maxHorizontalResolution = \"BLOCK\"\n".Replace("RADIUS", preset.DhRadius, StringComparison.Ordinal).Replace("QUALITY", preset.DhQuality, StringComparison.Ordinal);
			File.WriteAllText(path, contents);
			return;
		}
		string text = File.ReadAllText(path);
		text = ReplaceToml(text, "lodChunkRenderDistanceRadius", preset.DhRadius);
		text = ReplaceToml(text, "horizontalQuality", "\"" + preset.DhQuality + "\"");
		text = ReplaceToml(text, "verticalQuality", "\"" + preset.DhQuality + "\"");
		if (!text.Contains("maxHorizontalResolution", StringComparison.Ordinal))
		{
			text = text.TrimEnd() + "\nmaxHorizontalResolution = \"BLOCK\"\n";
		}
		File.WriteAllText(path, text);
	}

	private static string ReplaceToml(string text, string key, string value)
	{
		string[] array = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		bool flag = false;
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].TrimStart();
			if (text2.StartsWith(key, StringComparison.Ordinal))
			{
				string text3 = text2;
				int length = key.Length;
				if (text3.Substring(length, text3.Length - length).TrimStart().StartsWith('='))
				{
					text3 = array[i];
					length = text2.Length;
					string text4 = text3.Substring(0, text3.Length - length);
					array[i] = text4 + key + " = " + value;
					flag = true;
				}
			}
		}
		string text5 = string.Join('\n', array);
		if (!flag)
		{
			text5 = text5.TrimEnd() + "\n" + key + " = " + value + "\n";
		}
		return text5;
	}

	private static void SeedSodiumIfMissing(string path)
	{
		if (!File.Exists(path))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, "{\n  \"quality\": {\n    \"hidden_fluid_culling\": true,\n    \"improved_fluid_shaping\": false,\n    \"use_closest_point_entity_sort\": false,\n    \"pixel_filtering_mode\": \"NEAREST\"\n  },\n  \"performance\": {\n    \"chunk_builder_threads\": 0,\n    \"chunk_build_defer_mode\": \"ALWAYS\",\n    \"animate_only_visible_textures\": true,\n    \"use_entity_culling\": true,\n    \"use_fog_occlusion\": true,\n    \"use_block_face_culling\": true,\n    \"use_no_error_g_l_context\": true,\n    \"quad_splitting_mode\": \"SAFE\"\n  }\n}");
		}
	}
}
