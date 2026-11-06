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
	[InlineData(GraphicsTier.High, "12", "7", "112.0", "1")]
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
		Assert.DoesNotContain(GraphicsPack.NeverEnable, options["resourcePacks"]);
		Assert.Contains("file/norcom-alacrity-candle-glass-fix.zip", options["resourcePacks"]);
		Assert.Contains("key_key.attack:key.mouse.left", File.ReadAllText(Path.Combine(dir, "options.txt")));
		Dictionary<string, string> iris = ReadProperties(Path.Combine(dir, "config", "iris.properties"));
		Assert.Equal(irisShadow, iris["maxShadowRenderDistance"]);
		Assert.Equal("ComplementaryUnbound_r5.9.3.zip", iris["shaderPack"]);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		string shaderZip = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.zip.txt"));
		Assert.Equal(298, AssignmentCount(shader));
		Assert.Equal(298, AssignmentCount(shaderZip));
		Assert.Contains("shadowDistance=" + shadowDistance, shader);
		Assert.Contains("FANCY_GLASS=true", shader);
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
		Assert.Contains("COLORED_CANDLE_LIGHT=true", shader);
		Assert.Contains("FANCY_GLASS=true", shader);
		Assert.Equal("true", options["improvedTransparency"]);
		Assert.Equal("2", options["particles"]);
		Assert.Equal("1.0", options["entityDistanceScaling"]);
		Assert.Equal("1", options["graphicsMode"]);
		Assert.Equal("260", options["maxFps"]);
		Assert.Contains("LIGHTSHAFT_QUALI_DEFINE=2", shader);
		Assert.Contains("shadowDistance=112.0", shader);
		Assert.Contains("LIGHTSHAFT_QUALI_DEFINE=2", shader);
		Assert.Contains("LIGHTSHAFT_BEHAVIOUR=1", shader);
		Assert.Contains("LIGHTSHAFT_NOON_I=100", shader);
		Assert.Contains("BLOOM_ENABLED=1", shader);
		Assert.Contains("BLOOM_STRENGTH=0.075", shader);
		Assert.Contains("ENTITY_SHADOW=-1", shader);
		Assert.Contains("PLAYER_SHADOW=1", shader);
		Assert.Contains("LEAF_SHADOW_OPTIMISATION_DEFINE=1", shader);
		Assert.Contains("CLOUD_SHADOWS=false", shader);
		Assert.Contains("BLOCK_REFLECT_QUALITY=1", shader);
		Assert.Contains("WATER_REFLECT_QUALITY=2", shader);
		Assert.Contains("SSAO_QUALI_DEFINE=2", shader);
		Assert.Contains("SSAO_I=100", shader);
		Assert.Contains("COLORED_LIGHTING=128", shader);
		Assert.Contains("COLORED_CANDLE_LIGHT=true", shader);
		Assert.Contains("FANCY_GLASS=true", shader);
		Assert.Contains("TAA_SMOOTHING=2", shader);
		Assert.Equal("7", ReadProperties(Path.Combine(dir, "config", "iris.properties"))["maxShadowRenderDistance"]);
		string dh = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("lodChunkRenderDistanceRadius = 160", dh);
		Assert.Contains("horizontalQuality = \"MEDIUM\"", dh);
		Assert.Contains("verticalQuality = \"MEDIUM\"", dh);
		Assert.Contains("enableSsao = false", dh);
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
		Assert.Contains("profile=ULTRA", shader);
		Assert.Contains("shadowDistance=160.0", shader);
		Assert.Contains("COLORED_LIGHTING=192", shader);
		Assert.Contains("COLORED_CANDLE_LIGHT=true", shader);
		Assert.Equal("true", options["improvedTransparency"]);
		Assert.Contains("ANISOTROPIC_FILTER=4", shader);
		Assert.Contains("LIGHTSHAFT_QUALI_DEFINE=2", shader);
		Dictionary<string, string> iris = ReadProperties(Path.Combine(dir, "config", "iris.properties"));
		Assert.Equal("14", iris["maxShadowRenderDistance"]);
		string dh = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("lodChunkRenderDistanceRadius = 256", dh);
		Assert.Contains("horizontalQuality = \"HIGH\"", dh);
	}

	[Fact]
	public void RainStyleStaysAnEnumOnHighAndUltra()
	{
		Assert.Contains("RAIN_STYLE=1", GraphicsPack.ReadText("high/ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("RAIN_STYLE=1", GraphicsPack.ReadText("high/ComplementaryUnbound_r5.9.3.zip.txt"));
		Assert.Contains("RAIN_STYLE=1", GraphicsPack.ReadText("ultra/ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("RAIN_STYLE=1", GraphicsPack.ReadText("ultra/ComplementaryUnbound_r5.9.3.zip.txt"));
		Assert.DoesNotContain("RAIN_STYLE=false", GraphicsPack.ReadText("high/ComplementaryUnbound_r5.9.3.txt"));
		Assert.DoesNotContain("RAIN_STYLE=false", GraphicsPack.ReadText("ultra/ComplementaryUnbound_r5.9.3.txt"));
		Assert.Equal("RAIN_STYLE=1\n", ShaderScale.Coerce("RAIN_STYLE=false\n", GraphicsTier.High));
		Assert.Equal("RAIN_STYLE=2\n", ShaderScale.Coerce("RAIN_STYLE=2\n", GraphicsTier.Ultra));
		string dir = NewGame();
		new GraphicsService().Apply(dir, GraphicsTier.High);
		string applied = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("RAIN_STYLE=1", applied);
		Assert.DoesNotContain("RAIN_STYLE=false", applied);
	}

	[Fact]
	public void MarblePackIsWrittenAfterTheCandleOverlay()
	{
		string dir = NewGame();
		string packsDir = Path.Combine(dir, "resourcepacks");
		Directory.CreateDirectory(packsDir);
		File.WriteAllText(Path.Combine(packsDir, "Alacrity.zip"), "do-not-touch");
		File.WriteAllBytes(Path.Combine(packsDir, GraphicsPack.MarbleFile), new byte[] { 1, 2, 3 });
		new GraphicsService().Apply(dir, GraphicsTier.High);
		Assert.Equal("do-not-touch", File.ReadAllText(Path.Combine(packsDir, "Alacrity.zip")));
		string marble = Path.Combine(packsDir, GraphicsPack.MarbleFile);
		string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(marble))).ToLowerInvariant();
		Assert.Equal(GraphicsPack.MarbleSha256, sha);
		Assert.Equal(1334970, new FileInfo(marble).Length);
		string packs = ReadOptions(Path.Combine(dir, "options.txt"))["resourcePacks"];
		int alacrity = packs.IndexOf("file/Alacrity.zip", StringComparison.Ordinal);
		int candle = packs.IndexOf("file/" + GraphicsPack.OverlayFile, StringComparison.Ordinal);
		int marbleAt = packs.IndexOf("file/" + GraphicsPack.MarbleFile, StringComparison.Ordinal);
		Assert.True(alacrity >= 0 && alacrity < candle && candle < marbleAt);
		Assert.DoesNotContain("Patrix", packs);
		Assert.DoesNotContain(GraphicsPack.NeverEnable, packs);
		Assert.False(File.Exists(Path.Combine(packsDir, "norcom-held", GraphicsPack.MarbleFile)));
	}

	[Fact]
	public async Task PlayDownloadsMarbleWhenTheCopyIsMissing()
	{
		string dir = NewGame();
		byte[] zip;
		using (Stream stream = typeof(GraphicsPack).Assembly.GetManifestResourceStream("Norcom.Launcher.Core.GraphicsPack.norcom_marble.zip")!)
		{
			using MemoryStream copy = new MemoryStream();
			stream.CopyTo(copy);
			zip = copy.ToArray();
		}
		int hits = 0;
		using HttpClient http = new HttpClient(new MarbleHandler(zip, () => hits++));
		await KnownVisualAssets.EnsureMarbleAsync(http, dir, new TransferMeter(), CancellationToken.None);
		string dest = Path.Combine(dir, "resourcepacks", GraphicsPack.MarbleFile);
		string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dest))).ToLowerInvariant();
		Assert.Equal(GraphicsPack.MarbleSha256, sha);
		Assert.Equal(1, hits);
		await KnownVisualAssets.EnsureMarbleAsync(http, dir, new TransferMeter(), CancellationToken.None);
		Assert.Equal(1, hits);
	}

	[Fact]
	public void HandOffDropsThePackReadyLabel()
	{
		TransferMeter meter = new TransferMeter();
		meter.SetPhase("Pack ready", 0.92, 0.0);
		meter.HandOff();
		TransferSnapshot snap = meter.Snapshot();
		Assert.Equal("", snap.Label);
		Assert.Equal(1.0, snap.Fraction);
	}

	private sealed class MarbleHandler : HttpMessageHandler
	{
		private readonly byte[] _zip;
		private readonly Action _hit;

		public MarbleHandler(byte[] zip, Action hit)
		{
			_zip = zip;
			_hit = hit;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			_hit();
			Assert.Equal(GraphicsPack.MarbleUrl, request.RequestUri?.AbsoluteUri);
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(_zip)
			});
		}
	}

	[Fact]
	public void CandleOverlayIsHealedBesideAlacrityAndCullingStaysOff()
	{
		string dir = NewGame();
		string packs = Path.Combine(dir, "resourcepacks");
		Directory.CreateDirectory(packs);
		File.WriteAllText(Path.Combine(packs, "Alacrity.zip"), "do-not-touch");
		new GraphicsService().Apply(dir, GraphicsTier.Low);
		Assert.Equal("do-not-touch", File.ReadAllText(Path.Combine(packs, "Alacrity.zip")));
		string overlay = Path.Combine(packs, GraphicsPack.OverlayFile);
		Assert.True(File.Exists(overlay));
		string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(overlay))).ToLowerInvariant();
		Assert.Equal(GraphicsPack.OverlaySha256, sha);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("RP_MODE=1", shader);
		Assert.Contains("COLORED_CANDLE_LIGHT=false", shader);
		Assert.DoesNotContain(GraphicsPack.NeverEnable, File.ReadAllText(Path.Combine(dir, "options.txt")));
		string tier = File.ReadAllText(Path.Combine(dir, "norcom-graphics-tier.txt"));
		Assert.StartsWith("tier=Low", tier);
		Assert.Equal(tier, File.ReadAllText(Path.Combine(dir, "config", "norcom-tier.txt")));
	}

	[Fact]
	public void MediumHighUsesTheHighShaderProfile()
	{
		string dir = NewGame();
		new GraphicsService().Apply(dir, GraphicsTier.MediumHigh);
		string shader = File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"));
		Assert.Contains("profile=HIGH", shader);
		Assert.Contains("COLORED_LIGHTING=0", shader);
		Assert.Contains("COLORED_CANDLE_LIGHT=false", shader);
		Assert.Equal("false", ReadOptions(Path.Combine(dir, "options.txt"))["improvedTransparency"]);
	}

	[Theory]
	[InlineData("low", "LOW", "1", "0", "false", "false")]
	[InlineData("medium", "MEDIUM", "1", "0", "false", "false")]
	[InlineData("medium_high", "HIGH", "1", "0", "false", "false")]
	[InlineData("high", "HIGH", "1", "128", "true", "true")]
	[InlineData("ultra", "ULTRA", "1", "192", "true", "true")]
	[InlineData("normal", "HIGH", "1", "128", "true", "true")]
	public void EveryTierFileMatchesTheCandleMatrix(string folder, string profile, string rpMode, string lighting, string candle, string transparency)
	{
		string shader = GraphicsPack.ReadText(folder + "/ComplementaryUnbound_r5.9.3.txt");
		string shaderZip = GraphicsPack.ReadText(folder + "/ComplementaryUnbound_r5.9.3.zip.txt");
		Assert.Contains("profile=" + profile, shader);
		Assert.Contains("profile=" + profile, shaderZip);
		Assert.Contains("FANCY_GLASS=true", shader);
		Assert.Contains("FANCY_GLASS=true", shaderZip);
		Assert.Contains("RP_MODE=" + rpMode, shader);
		Assert.Contains("COLORED_LIGHTING=" + lighting, shader);
		Assert.Contains("COLORED_CANDLE_LIGHT=" + candle, shader);
		Assert.DoesNotContain("glass_pane_culling_fix", shader);
		string packs = GraphicsPack.ReadText(folder + "/resourcePacks.txt");
		Assert.Equal(GraphicsPack.AlacrityPacks, packs.Trim());
		Assert.DoesNotContain(GraphicsPack.NeverEnable, packs);
		Assert.Equal(transparency, GraphicsPack.ReadJsonMap(folder + "/options-patches.json")["improvedTransparency"]);
	}

	[Fact]
	public void Rtx3070MapsToHighNotUltra()
	{
		long vram = GraphicsAuto.DedicatedVram("NVIDIA GeForce RTX 3070", 4L * GraphicsAuto.Gib, 4L * GraphicsAuto.Gib);
		Assert.Equal(8L * GraphicsAuto.Gib, vram);
		GraphicsTier tier = GraphicsAuto.Choose(new PcHardware("NVIDIA GeForce RTX 3070", vram, 32L * GraphicsAuto.Gib, 16, Integrated: false));
		Assert.Equal(GraphicsTier.High, tier);
		long ambiguous = GraphicsAuto.DedicatedVram("NVIDIA GeForce RTX 3070", 5L * GraphicsAuto.Gib, 5L * GraphicsAuto.Gib);
		Assert.Equal(8L * GraphicsAuto.Gib, ambiguous);
		Assert.Equal(GraphicsTier.High, GraphicsAuto.Choose(new PcHardware("NVIDIA GeForce RTX 3070", 5L * GraphicsAuto.Gib, 16L * GraphicsAuto.Gib, 16, Integrated: false)));
	}

	[Fact]
	public void Sticky3070StaysHighWhenTheNextProbeLooksLikeMedium()
	{
		string dir = NewGame();
		File.WriteAllText(Path.Combine(dir, "norcom-graphics-tier.txt"), "tier=High\nsticky=3070\n");
		GraphicsPlay.Probe = () => new PcHardware("NVIDIA GeForce RTX 3070", 5L * GraphicsAuto.Gib, 16L * GraphicsAuto.Gib, 16, Integrated: false);
		try
		{
			Assert.Equal(GraphicsTier.High, GraphicsPlay.ApplyAuto(dir));
			Assert.Contains("sticky=3070", File.ReadAllText(Path.Combine(dir, "norcom-graphics-tier.txt")));
			GraphicsPlay.Probe = () => new PcHardware("Microsoft Basic Render Driver", 2L * GraphicsAuto.Gib, 16L * GraphicsAuto.Gib, 8, Integrated: true);
			Assert.Equal(GraphicsTier.High, GraphicsPlay.ApplyAuto(dir));
		}
		finally
		{
			GraphicsPlay.Probe = null;
		}
	}

	[Fact]
	public void PlayRaisesTheHeapThatOomedAt5632()
	{
		long sixteen = 16L * GraphicsAuto.Gib;
		Assert.Equal(8192, MemoryPlan.ForPlay(5632, sixteen, "NVIDIA GeForce RTX 3070"));
		Assert.Equal(8192, MemoryPlan.ForPlay(6144, sixteen, "NVIDIA GeForce RTX 3070"));
		Assert.Equal(4096, MemoryPlan.ForPlay(5632, 8L * GraphicsAuto.Gib, "NVIDIA GeForce RTX 3070"));
		Assert.Equal(10240, MemoryPlan.ForPlay(12288, 32L * GraphicsAuto.Gib, "NVIDIA GeForce RTX 4070"));
	}

	[Fact]
	public void FreshAnimationsCannotStackOnAlacrity()
	{
		string dir = NewGame();
		string packs = Path.Combine(dir, "resourcepacks");
		Directory.CreateDirectory(packs);
		File.WriteAllText(Path.Combine(packs, "Alacrity.zip"), "keep-alacrity");
		File.WriteAllText(Path.Combine(packs, "FreshAnimations.zip"), "stacked");
		File.WriteAllText(Path.Combine(dir, "options.txt"), "resourcePacks:[\"vanilla\",\"file/Alacrity.zip\",\"file/FreshAnimations.zip\"]\n");
		new GraphicsService().Apply(dir, GraphicsTier.High);
		string options = File.ReadAllText(Path.Combine(dir, "options.txt"));
		Assert.Contains(GraphicsPack.AlacrityPacks, options);
		Assert.DoesNotContain("FreshAnimations", options);
		Assert.DoesNotContain("Patrix", options);
		Assert.Equal("keep-alacrity", File.ReadAllText(Path.Combine(packs, "Alacrity.zip")));
		Assert.False(File.Exists(Path.Combine(packs, "FreshAnimations.zip")));
		Assert.Equal("stacked", File.ReadAllText(Path.Combine(packs, "norcom-held", "FreshAnimations.zip")));
	}

	[Theory]
	[InlineData(GraphicsTier.Low, "0.5")]
	[InlineData(GraphicsTier.Medium, "0.5")]
	[InlineData(GraphicsTier.MediumHigh, "0.5")]
	[InlineData(GraphicsTier.High, "0.5")]
	[InlineData(GraphicsTier.Ultra, "1.0")]
	public void ReflectionScaleStaysNumericForIris(GraphicsTier tier, string scale)
	{
		string dir = NewGame();
		new GraphicsService().Apply(dir, tier);
		foreach (string name in new string[2] { "ComplementaryUnbound_r5.9.3.txt", "ComplementaryUnbound_r5.9.3.zip.txt" })
		{
			Dictionary<string, string> shader = ReadProperties(Path.Combine(dir, "shaderpacks", name));
			Assert.Equal(scale, shader["REFLECTION_RES"]);
			Assert.Contains(".", shader["REFLECTION_RES"]);
			Assert.Equal("100", shader["WATERCOLOR_R"]);
			Assert.Equal("100", shader["WATERCOLOR_G"]);
			Assert.Equal("100", shader["WATERCOLOR_B"]);
			Assert.Equal("0.65", shader["COLORED_LIGHT_FOG_I"]);
			Assert.Equal("28", shader["NETHER_STORM_LOWER_ALT"]);
			Assert.Equal("192", shader["CLOUD_ALT1"]);
			Assert.Equal("1.00", shader["MOON_PHASE_FULL"]);
			Assert.Equal("0.25", shader["LIGHTSHAFT_SUNSET_SATURATION"]);
			Assert.Equal("false", shader["ATM_COLOR_MULTS"]);
			Assert.Equal("false", shader["LIGHT_COLOR_MULTS"]);
			Assert.Equal("false", shader["WB_FOV_SCALED"]);
			Assert.Equal("false", shader["WORLD_OUTLINE_SCALED"]);
			Assert.Equal("true", shader["FANCY_GLASS"]);
		}
	}

	[Fact]
	public void CoerceRewritesFalseBeforeTextureScaleParseInt()
	{
		string bad = "profile=HIGH\nREFLECTION_RES=false\nWATERCOLOR_R=false\nCOLORED_LIGHT_FOG_I=false\nNETHER_STORM_LOWER_ALT=false\nCLOUD_ALT1=false\nMOON_PHASE_FULL=false\nLIGHTSHAFT_SUNSET_SATURATION=false\nFANCY_GLASS=true\nshadowDistance=112.0\n";
		string coerced = ShaderScale.Coerce(bad, GraphicsTier.High);
		Assert.Contains("REFLECTION_RES=0.5", coerced);
		Assert.Contains("WATERCOLOR_R=100", coerced);
		Assert.Contains("COLORED_LIGHT_FOG_I=0.65", coerced);
		Assert.Contains("NETHER_STORM_LOWER_ALT=28", coerced);
		Assert.Contains("CLOUD_ALT1=192", coerced);
		Assert.Contains("MOON_PHASE_FULL=1.00", coerced);
		Assert.Contains("LIGHTSHAFT_SUNSET_SATURATION=0.25", coerced);
		Assert.Contains("FANCY_GLASS=true", coerced);
		Assert.Contains("shadowDistance=112.0", coerced);
		Assert.DoesNotContain("REFLECTION_RES=false", coerced);
		Assert.Contains("REFLECTION_RES=1.0", ShaderScale.Coerce("REFLECTION_RES=false\n", GraphicsTier.Ultra));
		Assert.Contains("REFLECTION_RES=0.5", ShaderScale.Coerce("REFLECTION_RES=false\n", GraphicsTier.Low));
		Assert.Contains("REFLECTION_RES=0.5", ShaderScale.Coerce("REFLECTION_RES=1\n", GraphicsTier.High));
		Assert.Contains("REFLECTION_RES=0.5", ShaderScale.Coerce("REFLECTION_RES=0.5\n", GraphicsTier.High));
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
