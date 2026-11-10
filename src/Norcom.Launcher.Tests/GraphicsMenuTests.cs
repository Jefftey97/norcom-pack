using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class GraphicsMenuTests
{
	[Fact]
	public void TheSamePcGetsDifferentPresetsPerGame()
	{
		MachineSpecs specs = Pc(vramGb: 8, ramGb: 16, cores: 8, mhz: 3600, "RTX 3070", 1920, 1080, 144);
		PresetChoice minecraft = GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), specs);
		PresetChoice zomboid = GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("zomboid"), specs);
		Assert.Equal("Medium-High", minecraft.Preset);
		Assert.Equal("Ultra", zomboid.Preset);
		Assert.Contains("Minecraft", minecraft.Reason);
		Assert.Contains("RTX 3070", minecraft.Reason);
		Assert.Contains("144", minecraft.Reason);
		Assert.Contains("Zomboid", zomboid.Reason);
	}

	[Fact]
	public void UnreadableSpecsFallBackToLow()
	{
		MachineSpecsProbe.Override = () => throw new InvalidOperationException("no adapter");
		try
		{
			MachineSpecs specs = MachineSpecsProbe.Read();
			Assert.Contains("detection failed", specs.Gaps[0]);
			PresetChoice choice = GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), specs);
			Assert.Equal("Low", choice.Preset);
			Assert.Contains("Low", choice.Reason);
		}
		finally
		{
			MachineSpecsProbe.Override = null;
		}
	}

	[Fact]
	public void LinuxProbeDoesNotThrow()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}
		MachineSpecs specs = MachineSpecsProbe.Read();
		Assert.True(specs.LogicalCores > 0);
		Assert.Contains(specs.Gaps, gap => gap.Contains("Windows", StringComparison.Ordinal));
		Assert.Equal("Low", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), specs).Preset);
	}

	[Fact]
	public void ProfilesForTwoGamesStaySeparate()
	{
		string home = NewDir();
		GraphicsProfileStore store = new GraphicsProfileStore();
		store.Put("minecraft", new SavedGraphicsProfile { Preset = "Ultra", Auto = false, Reason = "chosen" });
		store.Put("zomboid", new SavedGraphicsProfile { Preset = "Medium", Auto = true, Reason = "recommended" });
		store.Save(home);
		GraphicsProfileStore loaded = GraphicsProfileStore.Load(home);
		Assert.Equal("Ultra", loaded.Find("minecraft").Preset);
		Assert.False(loaded.Find("minecraft").Auto);
		Assert.Equal("Medium", loaded.Find("zomboid").Preset);
		Assert.True(loaded.Find("zomboid").Auto);
	}

	[Fact]
	public void MinecraftRoundTripKeepsOtherOptionsAndBacksUpOnce()
	{
		string game = NewDir();
		File.WriteAllText(Path.Combine(game, "options.txt"), "version:5023\nrenderDistance:6\nkey_key.attack:key.mouse.left\nfoo:bar\n");
		Directory.CreateDirectory(Path.Combine(game, "config"));
		File.WriteAllText(Path.Combine(game, "config", "sodium-options.json"), "{\"performance\":{\"chunk_builder_threads\":4,\"use_entity_culling\":true},\"quality\":{\"pixel_filtering_mode\":\"NEAREST\"}}");
		File.WriteAllText(Path.Combine(game, "config", "iris.properties"), "# keep\nshaderPack=ComplementaryUnbound_r5.9.3.zip\nmaxShadowRenderDistance=4\n");
		SavedGraphicsProfile profile = new SavedGraphicsProfile { Preset = "Ultra", Auto = true };
		string first = GameSettingsWriter.Apply(GameGraphicsCatalog.Require("minecraft"), profile, game, NewDir());
		string second = GameSettingsWriter.Apply(GameGraphicsCatalog.Require("minecraft"), profile, game, NewDir());
		string options = File.ReadAllText(Path.Combine(game, "options.txt"));
		Assert.Contains("renderDistance:24", options);
		Assert.Contains("key_key.attack:key.mouse.left", options);
		Assert.Contains("foo:bar", options);
		Assert.Equal(File.ReadAllText(Path.Combine(game, "options.txt.norcom-backup")), "version:5023\nrenderDistance:6\nkey_key.attack:key.mouse.left\nfoo:bar\n");
		Assert.Contains("applied", first);
		Assert.Contains("options.txt", second);
		using JsonDocument sodium = JsonDocument.Parse(File.ReadAllText(Path.Combine(game, "config", "sodium-options.json")));
		Assert.Equal(0, sodium.RootElement.GetProperty("performance").GetProperty("chunk_builder_threads").GetInt32());
		Assert.True(sodium.RootElement.GetProperty("performance").GetProperty("use_entity_culling").GetBoolean());
		Assert.Equal("NEAREST", sodium.RootElement.GetProperty("quality").GetProperty("pixel_filtering_mode").GetString());
		string iris = File.ReadAllText(Path.Combine(game, "config", "iris.properties"));
		Assert.Contains("shaderPack=ComplementaryUnbound_r5.9.3.zip", iris);
		Assert.Contains("# keep", iris);
		Assert.Contains("maxShadowRenderDistance=32", iris);
	}

	[Fact]
	public void RunningGameIsNotOverwritten()
	{
		string game = NewDir();
		string options = Path.Combine(game, "options.txt");
		File.WriteAllText(options, "renderDistance:6\n");
		GameSession.Probe = _ => true;
		try
		{
			string note = GameLaunchGraphics.ApplyBeforeLaunch("minecraft", game, NewDir(), "", NewDir());
			Assert.Contains("running", note);
			Assert.Equal("renderDistance:6\n", File.ReadAllText(options));
			Assert.False(File.Exists(options + ".norcom-backup"));
		}
		finally
		{
			GameSession.Probe = null;
		}
	}

	[Fact]
	public void ZomboidMediumDoesNotTouchMinecraft()
	{
		string user = NewDir();
		string zomboid = Path.Combine(user, "Zomboid");
		Directory.CreateDirectory(zomboid);
		File.WriteAllText(Path.Combine(zomboid, "options.ini"), "language=EN\nframeRate=144\nfullscreen=true\n");
		string minecraft = NewDir();
		File.WriteAllText(Path.Combine(minecraft, "options.txt"), "renderDistance:9\n");
		GameSettingsWriter.Apply(GameGraphicsCatalog.Require("zomboid"), new SavedGraphicsProfile { Preset = "Medium", Auto = true }, minecraft, user);
		string ini = File.ReadAllText(Path.Combine(zomboid, "options.ini"));
		Assert.Contains("language=EN", ini);
		Assert.Contains("frameRate=60", ini);
		Assert.Equal("renderDistance:9\n", File.ReadAllText(Path.Combine(minecraft, "options.txt")));
	}

	[Fact]
	public void ModDiscoveryUsesSchemaAndLeavesUnknownFormatsAlone()
	{
		string config = NewDir();
		File.WriteAllText(Path.Combine(config, "sodium-options.json"), "{\"performance\":{\"chunk_builder_threads\":2},\"quality\":{\"pixel_filtering_mode\":\"LINEAR\"}}");
		File.WriteAllText(Path.Combine(config, "notes.cfg"), "mystery=1\n");
		string index = "[[files]]\nfile = \"config/sodium-options.json\"\npreserve = true\n";
		string schema = "{\"files\":[{\"path\":\"sodium-options.json\",\"settings\":[{\"key\":\"performance.chunk_builder_threads\",\"label\":\"Chunk threads\",\"help\":\"0 lets Sodium choose.\",\"kind\":\"slider\",\"min\":0,\"max\":16}]}]}";
		IReadOnlyList<DiscoveredModFile> files = ModSettingsCatalog.Discover(config, index, schema);
		DiscoveredModFile sodium = Assert.Single(files, file => file.RelativePath == "sodium-options.json");
		Assert.True(sodium.Preserved);
		Assert.False(sodium.ReadOnly);
		DiscoveredSetting threads = Assert.Single(sodium.Settings, setting => setting.Key == "performance.chunk_builder_threads");
		Assert.Equal("Chunk threads", threads.Label);
		Assert.Equal("slider", threads.Kind);
		Assert.Equal(16, threads.Max);
		DiscoveredModFile notes = Assert.Single(files, file => file.RelativePath == "notes.cfg");
		Assert.True(notes.ReadOnly);
		Assert.Equal("unknown", notes.Format);
		Assert.True(ModSettingsCatalog.TrySave(config, sodium, new Dictionary<string, string> { ["performance.chunk_builder_threads"] = "5" }, out string error));
		Assert.Equal("", error);
		using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(config, "sodium-options.json")));
		Assert.Equal(5, saved.RootElement.GetProperty("performance").GetProperty("chunk_builder_threads").GetInt32());
		Assert.Equal("LINEAR", saved.RootElement.GetProperty("quality").GetProperty("pixel_filtering_mode").GetString());
		Assert.False(ModSettingsCatalog.TrySave(config, notes, new Dictionary<string, string>(), out error));
		Assert.Contains("read-only", error);
	}

	private static MachineSpecs Pc(int vramGb, int ramGb, int cores, int mhz, string gpu, int width, int height, int refresh)
	{
		return new MachineSpecs
		{
			GpuName = gpu,
			VramBytes = vramGb * 1073741824L,
			RamBytes = ramGb * 1073741824L,
			LogicalCores = cores,
			CpuMhz = mhz,
			DisplayWidth = width,
			DisplayHeight = height,
			RefreshHz = refresh
		};
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-graphics-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
