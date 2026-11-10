using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class GraphicsMenuTests
{
	[Fact]
	public void TheSamePcGetsTheSamePresetForBothGames()
	{
		MachineSpecs specs = Pc(vramGb: 8, ramGb: 16, cores: 8, mhz: 3600, "RTX 3070", 1920, 1080, 144);
		PresetChoice minecraft = GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), specs);
		PresetChoice zomboid = GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("zomboid"), specs);
		Assert.Equal("Medium-High", minecraft.Preset);
		Assert.Equal(minecraft.Preset, zomboid.Preset);
		Assert.Equal("Ultra", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), Pc(vramGb: 12, ramGb: 32, cores: 16, mhz: 3600, "RTX 4080", 2560, 1440, 144)).Preset);
		Assert.Equal("Ultra", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("zomboid"), Pc(vramGb: 12, ramGb: 32, cores: 16, mhz: 3600, "RTX 4080", 2560, 1440, 144)).Preset);
		Assert.Equal("Medium", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), Pc(vramGb: 4, ramGb: 16, cores: 8, mhz: 3200, "GTX 1650", 1920, 1080, 60)).Preset);
		Assert.Equal("Medium", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("zomboid"), Pc(vramGb: 4, ramGb: 16, cores: 8, mhz: 3200, "GTX 1650", 1920, 1080, 60)).Preset);
	}

	[Fact]
	public void Rtx3070ClassPcIsMediumHighForBothGames()
	{
		MachineSpecs lab = new MachineSpecs
		{
			GpuName = "NVIDIA GeForce RTX 3070",
			VramBytes = 8032L * 1024L * 1024L,
			RamBytes = (long)(31.9d * 1073741824L),
			LogicalCores = 16,
			CpuMhz = 3800,
			DisplayWidth = 2560,
			DisplayHeight = 1440,
			RefreshHz = 144
		};
		Assert.Equal("Medium-High", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("minecraft"), lab).Preset);
		Assert.Equal("Medium-High", GameGraphicsCatalog.Recommend(GameGraphicsCatalog.Require("zomboid"), lab).Preset);
	}

	[Fact]
	public void FirstRunSavesTheRecommendedPresetAndKeepsAManualChoice()
	{
		MachineSpecs lab = new MachineSpecs
		{
			GpuName = "NVIDIA GeForce RTX 3070",
			VramBytes = 8032L * 1024L * 1024L,
			RamBytes = (long)(31.9d * 1073741824L),
			LogicalCores = 16,
			DisplayWidth = 2560,
			DisplayHeight = 1440,
			RefreshHz = 144
		};
		string home = NewDir();
		(SavedGraphicsProfile minecraft, PresetChoice recommended) = GraphicsProfileStore.Resolve(home, "minecraft", lab);
		Assert.Equal("Medium-High", recommended.Preset);
		Assert.Equal("Medium-High", minecraft.Preset);
		Assert.True(minecraft.Auto);
		Assert.Equal("Medium-High", GraphicsProfileStore.Load(home).Find("minecraft").Preset);
		GraphicsProfileStore stale = GraphicsProfileStore.Load(home);
		stale.Put("zomboid", new SavedGraphicsProfile { Preset = "Low", Auto = true, Reason = "old" });
		stale.Save(home);
		(SavedGraphicsProfile zomboid, _) = GraphicsProfileStore.Resolve(home, "zomboid", lab);
		Assert.Equal("Medium-High", zomboid.Preset);
		Assert.True(zomboid.Auto);
		Assert.Equal("Medium-High", GraphicsProfileStore.Load(home).Find("zomboid").Preset);
		GraphicsProfileStore manual = GraphicsProfileStore.Load(home);
		manual.Put("zomboid", new SavedGraphicsProfile { Preset = "Low", Auto = false, Reason = "chosen" });
		manual.Save(home);
		(SavedGraphicsProfile kept, _) = GraphicsProfileStore.Resolve(home, "zomboid", lab);
		Assert.Equal("Low", kept.Preset);
		Assert.False(kept.Auto);
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

	[Fact]
	public void ModSaveRefusesSymlinksTraversalAndARunningGame()
	{
		string root = NewDir();
		string config = Path.Combine(root, "config");
		Directory.CreateDirectory(config);
		string outside = Path.Combine(root, "outside.json");
		File.WriteAllText(outside, "{\"keep\":1}");
		File.CreateSymbolicLink(Path.Combine(config, "linked.json"), outside);
		File.WriteAllText(Path.Combine(root, "escape.txt"), "keep");
		DiscoveredModFile link = new DiscoveredModFile { RelativePath = "linked.json", Format = "json" };
		DiscoveredModFile escape = new DiscoveredModFile { RelativePath = "../escape.txt", Format = "json" };
		Assert.False(ModSettingsCatalog.TrySave(config, link, new Dictionary<string, string> { ["keep"] = "2" }, out string linkError));
		Assert.False(string.IsNullOrEmpty(linkError));
		Assert.Equal("{\"keep\":1}", File.ReadAllText(outside));
		Assert.False(ModSettingsCatalog.TrySave(config, escape, new Dictionary<string, string> { ["keep"] = "2" }, out string escapeError));
		Assert.False(string.IsNullOrEmpty(escapeError));
		Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "escape.txt")));
		File.WriteAllText(Path.Combine(config, "ok.json"), "{\"threads\":1}");
		DiscoveredModFile ok = new DiscoveredModFile { RelativePath = "ok.json", Format = "json" };
		GameSession.Probe = _ => true;
		try
		{
			Assert.False(ModSettingsCatalog.TrySave(config, ok, new Dictionary<string, string> { ["threads"] = "4" }, out string running));
			Assert.Contains("running", running);
			Assert.Equal("{\"threads\":1}", File.ReadAllText(Path.Combine(config, "ok.json")));
		}
		finally
		{
			GameSession.Probe = null;
		}
	}

	[Fact]
	public void MalformedFilesAreNotRewritten()
	{
		string config = NewDir();
		File.WriteAllText(Path.Combine(config, "broken.json"), "{");
		DiscoveredModFile json = new DiscoveredModFile { RelativePath = "broken.json", Format = "json" };
		Assert.False(ModSettingsCatalog.TrySave(config, json, new Dictionary<string, string> { ["a"] = "1" }, out string jsonError));
		Assert.Contains("unchanged", jsonError);
		Assert.Equal("{", File.ReadAllText(Path.Combine(config, "broken.json")));
		File.WriteAllText(Path.Combine(config, "broken.properties"), "this is not a property\n");
		DiscoveredModFile properties = new DiscoveredModFile { RelativePath = "broken.properties", Format = "properties" };
		Assert.False(ModSettingsCatalog.TrySave(config, properties, new Dictionary<string, string> { ["a"] = "1" }, out _));
		Assert.Equal("this is not a property\n", File.ReadAllText(Path.Combine(config, "broken.properties")));
		string game = NewDir();
		File.WriteAllText(Path.Combine(game, "options.txt"), "not options\n");
		string result = GameSettingsWriter.Apply(GameGraphicsCatalog.Require("minecraft"), new SavedGraphicsProfile { Preset = "Low", Auto = true }, game, NewDir());
		Assert.Contains("unchanged", result);
		Assert.Equal("not options\n", File.ReadAllText(Path.Combine(game, "options.txt")));
	}

	[Fact]
	public void TomlTableKeyIsEditedInPlace()
	{
		string config = NewDir();
		string original = "# keep\n[quality]\ndistance = 12 # chunks\nenabled = true\n\"my.key\" = \"old\"\ncamera = { distance = 4 }\n\n[client]\nname = \"keep\"\n";
		File.WriteAllText(Path.Combine(config, "view.toml"), original);
		IReadOnlyList<DiscoveredModFile> found = ModSettingsCatalog.Discover(config, "", "");
		DiscoveredModFile file = Assert.Single(found);
		Assert.Contains(file.Settings, setting => setting.Key == "quality.distance" && setting.Value == "12");
		Assert.Contains(file.Settings, setting => setting.Key == "quality.\"my.key\"" && !setting.ReadOnly);
		Assert.Contains(file.Settings, setting => setting.Key == "quality.camera" && setting.ReadOnly);
		Assert.True(ModSettingsCatalog.TrySave(config, file, new Dictionary<string, string> { ["quality.distance"] = "2", ["quality.\"my.key\""] = "new" }, out string error));
		Assert.Equal("", error);
		string updated = File.ReadAllText(Path.Combine(config, "view.toml"));
		Assert.Equal("# keep\n[quality]\ndistance = 2 # chunks\nenabled = true\n\"my.key\" = \"new\"\ncamera = { distance = 4 }\n\n[client]\nname = \"keep\"\n", updated);
		Assert.DoesNotContain("quality.distance=", updated);
		File.WriteAllText(Path.Combine(config, "broken.toml"), "[quality\ndistance = 1\n");
		DiscoveredModFile broken = new DiscoveredModFile { RelativePath = "broken.toml", Format = "toml" };
		Assert.False(ModSettingsCatalog.TrySave(config, broken, new Dictionary<string, string> { ["quality.distance"] = "2" }, out _));
		Assert.Equal("[quality\ndistance = 1\n", File.ReadAllText(Path.Combine(config, "broken.toml")));
	}

	[Fact]
	public void PackManagedFilesAreReadOnly()
	{
		string config = NewDir();
		File.WriteAllText(Path.Combine(config, "plain.json"), "{\"a\":1}");
		string index = "[[files]]\nfile = \"config/plain.json\"\n[[files]]\nfile = \"config/sodium-options.json\"\npreserve = true\n";
		File.WriteAllText(Path.Combine(config, "sodium-options.json"), "{\"performance\":{\"chunk_builder_threads\":1}}");
		IReadOnlyList<DiscoveredModFile> files = ModSettingsCatalog.Discover(config, index, "");
		DiscoveredModFile plain = Assert.Single(files, file => file.RelativePath == "plain.json");
		Assert.True(plain.ReadOnly);
		Assert.Contains("Managed by the pack", plain.Note);
		Assert.False(ModSettingsCatalog.TrySave(config, plain, new Dictionary<string, string> { ["a"] = "2" }, out _));
		Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(config, "plain.json")));
		DiscoveredModFile sodium = Assert.Single(files, file => file.RelativePath == "sodium-options.json");
		Assert.False(sodium.ReadOnly);
		Assert.True(sodium.Preserved);
	}

	[Fact]
	public void GraphicsWriteRefusesASymlink()
	{
		string root = NewDir();
		string game = Path.Combine(root, "game");
		Directory.CreateDirectory(game);
		string outside = Path.Combine(root, "outside.txt");
		File.WriteAllText(outside, "renderDistance:4\n");
		File.CreateSymbolicLink(Path.Combine(game, "options.txt"), outside);
		string result = GameSettingsWriter.Apply(GameGraphicsCatalog.Require("minecraft"), new SavedGraphicsProfile { Preset = "Ultra", Auto = true }, game, NewDir());
		Assert.Contains("unchanged", result);
		Assert.Equal("renderDistance:4\n", File.ReadAllText(outside));
		Assert.False(Directory.EnumerateFiles(game, "*.norcom-partial").Any());
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
