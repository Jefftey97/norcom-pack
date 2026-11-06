using Xunit;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Tests;

public class GraphicsWriterTests
{
	[Theory]
	[InlineData(GraphicsTier.MediumHigh)]
	[InlineData(GraphicsTier.High)]
	[InlineData(GraphicsTier.Ultra)]
	public void FabulousTiersWriteFabulousAndBiomeBlend7(GraphicsTier tier)
	{
		string dir = NewGame();
		File.WriteAllText(Path.Combine(dir, "options.txt"), "graphicsMode:1\nbiomeBlendRadius:2\nmipmapLevels:4\n");
		new GraphicsService().Apply(dir, tier);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal("2", options["graphicsMode"]);
		Assert.Equal("7", options["biomeBlendRadius"]);
		Assert.Equal("4", options["mipmapLevels"]);
		Assert.Equal("false", options["enableVsync"]);
		Assert.Equal("260", options["maxFps"]);
		AssertSameTierNote(dir, tier);
	}

	[Fact]
	public void HighKeepsThePassingPreset()
	{
		string dir = NewGame();
		File.WriteAllText(Path.Combine(dir, "options.txt"), "graphicsMode:1\nbiomeBlendRadius:2\n");
		GraphicsPreset preset = new GraphicsService().Apply(dir, GraphicsTier.High);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal(14, preset.RenderDistance);
		Assert.Equal(10, preset.SimulationDistance);
		Assert.Equal("1.0", preset.EntityDistance);
		Assert.Equal("128", preset.DhRadius);
		Assert.Equal("MEDIUM", preset.DhQuality);
		Assert.Equal("14", options["renderDistance"]);
		Assert.Equal("10", options["simulationDistance"]);
		Assert.Equal("1.0", options["entityDistanceScaling"]);
		Assert.Equal("2", options["graphicsMode"]);
		Assert.Equal("7", options["biomeBlendRadius"]);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("profile=HIGH", shader);
		string sodium = File.ReadAllText(Path.Combine(dir, "config", "sodium-options.json"));
		Assert.Contains("\"chunk_builder_threads\": 0", sodium);
		string packs = options["resourcePacks"];
		Assert.Contains("Patrix_26.3_32x_basic.zip", packs);
		Assert.DoesNotContain("Alacrity", packs);
	}

	[Fact]
	public void Rtx3070MapsToHighNotUltra()
	{
		GraphicsTier tier = GraphicsAuto.Choose(new PcHardware("NVIDIA GeForce RTX 3070", 8L * GraphicsAuto.Gib, 32L * GraphicsAuto.Gib, 16, Integrated: false));
		Assert.Equal(GraphicsTier.High, tier);
	}

	[Theory]
	[InlineData(GraphicsTier.Low)]
	[InlineData(GraphicsTier.Medium)]
	public void LowerTiersStayFancyWithBlend2(GraphicsTier tier)
	{
		string dir = NewGame();
		File.WriteAllText(Path.Combine(dir, "options.txt"), "graphicsMode:2\nbiomeBlendRadius:7\n");
		new GraphicsService().Apply(dir, tier);
		Dictionary<string, string> options = ReadOptions(Path.Combine(dir, "options.txt"));
		Assert.Equal("1", options["graphicsMode"]);
		Assert.Equal("2", options["biomeBlendRadius"]);
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
		Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in File.ReadAllLines(path))
		{
			int split = line.IndexOf(':');
			if (split > 0)
			{
				map[line[..split]] = line[(split + 1)..];
			}
		}
		return map;
	}
}
