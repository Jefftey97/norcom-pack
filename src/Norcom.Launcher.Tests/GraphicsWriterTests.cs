using Xunit;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Tests;

public class GraphicsWriterTests
{
	[Theory]
	[InlineData(GraphicsTier.Low, "8", "8", "64.0", "0")]
	[InlineData(GraphicsTier.Medium, "10", "10", "96.0", "1")]
	[InlineData(GraphicsTier.MediumHigh, "12", "12", "112.0", "1")]
	[InlineData(GraphicsTier.High, "12", "12", "128.0", "1")]
	[InlineData(GraphicsTier.Ultra, "14", "14", "160.0", "1")]
	public void EachTierAppliesTheAlacrityPack(GraphicsTier tier, string renderDistance, string irisShadow, string shadowDistance, string graphicsMode)
	{
		string dir = NewGame();
		File.WriteAllText(Path.Combine(dir, "options.txt"), "graphicsMode:2\nbiomeBlendRadius:7\nrenderClouds:true\nkey_key.attack:key.mouse.left\nresourcePacks:[\"file/Patrix_26.3_32x_basic.zip\"]\n");
		new GraphicsService().Apply(dir, tier);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal("true", options["fullscreen"]);
		Assert.Equal("false", options["exclusiveFullscreen"]);
		Assert.Equal("false", options["enableVsync"]);
		Assert.Equal("260", options["maxFps"]);
		Assert.Equal("false", options["renderClouds"]);
		Assert.Equal(renderDistance, options["renderDistance"]);
		Assert.Equal(graphicsMode, options["graphicsMode"]);
		Assert.Equal(GraphicsPack.AlacrityPacks, options["resourcePacks"]);
		Assert.DoesNotContain("Patrix", options["resourcePacks"]);
		Assert.Contains("key_key.attack:key.mouse.left", File.ReadAllText(Path.Combine(dir, "options.txt")));
		Dictionary<string, string> iris = ReadProperties(Path.Combine(dir, "config", "iris.properties"));
		Assert.Equal(irisShadow, iris["maxShadowRenderDistance"]);
		Assert.Equal("ComplementaryUnbound_r5.9.3.zip", iris["shaderPack"]);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		string shaderZip = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip.txt"));
		Assert.Equal(298, AssignmentCount(shader));
		Assert.Equal(298, AssignmentCount(shaderZip));
		Assert.Contains("shadowDistance=" + shadowDistance, shader);
		Assert.Contains("no Patrix", shader);
		string sodium = File.ReadAllText(Path.Combine(dir, "config", "sodium-options.json"));
		Assert.Contains("\"chunk_builder_threads\": 0", sodium);
		string dh = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("enableSsao = false", dh);
		AssertSameTierNote(dir, tier);
	}

	[Fact]
	public void HighAppliesTheHighFolder()
	{
		string dir = NewGame();
		GraphicsPreset preset = new GraphicsService().Apply(dir, GraphicsTier.High);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal(12, preset.RenderDistance);
		Assert.Equal(10, preset.SimulationDistance);
		Assert.Equal("12", options["renderDistance"]);
		Assert.Equal("10", options["simulationDistance"]);
		Assert.Equal("2", options["biomeBlendRadius"]);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("profile=HIGH", shader);
		Assert.Contains("SHADOW_QUALITY=2", shader);
		Assert.Contains("COLORED_LIGHTING=128", shader);
		Assert.Contains("LIGHTSHAFT_QUALI_DEFINE=2", shader);
		Assert.Contains("shadowDistance=128.0", shader);
		string dh = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("lodChunkRenderDistanceRadius = 192", dh);
	}

	[Fact]
	public void UltraAppliesTheUltraFolder()
	{
		string dir = NewGame();
		GraphicsPreset preset = new GraphicsService().Apply(dir, GraphicsTier.Ultra);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal(14, preset.RenderDistance);
		Assert.Equal("14", options["renderDistance"]);
		Assert.Equal("false", options["exclusiveFullscreen"]);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("shadowDistance=160.0", shader);
		Assert.Contains("COLORED_LIGHTING=192", shader);
		Assert.Contains("ANISOTROPIC_FILTER=4", shader);
		Assert.Contains("LIGHTSHAFT_QUALI_DEFINE=2", shader);
		Dictionary<string, string> iris = ReadProperties(Path.Combine(dir, "config", "iris.properties"));
		Assert.Equal("14", iris["maxShadowRenderDistance"]);
		string dh = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("lodChunkRenderDistanceRadius = 256", dh);
		Assert.Contains("horizontalQuality = \"HIGH\"", dh);
	}

	[Fact]
	public void Rtx3070MapsToHighNotUltra()
	{
		long vram = GraphicsAuto.DedicatedVram("NVIDIA GeForce RTX 3070", 4L * GraphicsAuto.Gib, 4L * GraphicsAuto.Gib);
		Assert.Equal(8L * GraphicsAuto.Gib, vram);
		GraphicsTier tier = GraphicsAuto.Choose(new PcHardware("NVIDIA GeForce RTX 3070", vram, 32L * GraphicsAuto.Gib, 16, Integrated: false));
		Assert.Equal(GraphicsTier.High, tier);
	}

	[Fact]
	public void TwelveGigabyteCardsMapToUltra()
	{
		GraphicsTier tier = GraphicsAuto.Choose(new PcHardware("NVIDIA GeForce RTX 4070", 12L * GraphicsAuto.Gib, 16L * GraphicsAuto.Gib, 8, Integrated: false));
		Assert.Equal(GraphicsTier.Ultra, tier);
	}

	private static int AssignmentCount(string shader)
	{
		int count = 0;
		foreach (string line in shader.Split('\n'))
		{
			string text = line.Trim();
			if (text.Length > 0 && !text.StartsWith('#') && text.Contains('='))
			{
				count++;
			}
		}
		return count;
	}

	private static void AssertSameTierNote(string dir, GraphicsTier tier)
	{
		string config = File.ReadAllText(Path.Combine(dir, "config", "norcom-tier.txt"));
		string root = File.ReadAllText(Path.Combine(dir, "norcom-graphics-tier.txt"));
		Assert.Equal(config, root);
		Assert.StartsWith("tier=" + tier, config);
	}

	private static string NewGame()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-gfx-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static Dictionary<string, string> ReadOptions(string path)
	{
		return ReadProperties(path, ':');
	}

	private static Dictionary<string, string> ReadProperties(string path, char split = '=')
	{
		Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in File.ReadAllLines(path))
		{
			int index = line.IndexOf(split);
			if (index > 0 && !line.TrimStart().StartsWith('#'))
			{
				map[line[..index].Trim()] = line[(index + 1)..].Trim();
			}
		}
		return map;
	}
}
