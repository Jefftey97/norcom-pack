using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class DistantHorizonsDefaultsTests
{
	[Fact]
	public void PlayDoesNotCreateDistantHorizonsUntilAPresetIsClicked()
	{
		string dir = NewDir();
		Assert.False(DistantHorizonsDefaults.EnsureFresh(dir, GraphicsTier.High));
		Assert.False(File.Exists(Path.Combine(dir, "config", "DistantHorizons.toml")));
		DistantHorizonsDefaults.ApplyPreset(dir, GraphicsTier.High);
		string created = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("enableTexturedLods = true", created);
		Assert.Contains("maxTexturedLodDetailLevel = 4", created);
		Assert.Contains("enableNoiseTexture = true", created);
		Assert.Contains("noiseIntensity = \"0.1\"", created);
		Assert.Contains("maxHorizontalResolution = \"BLOCK\"", created);
		Assert.Contains("verticalQuality = \"HIGH\"", created);
		Assert.Contains("horizontalQuality = \"HIGH\"", created);
		Assert.Contains("enableSsao = true", created);
		File.WriteAllText(Path.Combine(dir, "config", "DistantHorizons.toml"), "# mine\nlodChunkRenderDistanceRadius = 32\n[client.advanced.graphics.quality]\nhorizontalQuality = \"LOW\"\n");
		string edited = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.False(DistantHorizonsDefaults.EnsureFresh(dir, GraphicsTier.Ultra));
		Assert.Equal(edited, File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml")));
	}

	[Fact]
	public void PresetEditsOnlyTheDistantHorizonsKeys()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "config"));
		string original = "# keep\nlodChunkRenderDistanceRadius = 32\n[client.advanced.graphics.quality]\nhorizontalQuality = \"LOW\"\nverticalQuality = \"LOW\"\n";
		File.WriteAllText(Path.Combine(dir, "config", "DistantHorizons.toml"), original);
		DistantHorizonsDefaults.ApplyPreset(dir, GraphicsTier.Low);
		string updated = File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml"));
		Assert.Contains("# keep", updated);
		Assert.Contains("lodChunkRenderDistanceRadius = 32", updated);
		Assert.Contains("verticalQuality = \"MEDIUM\"", updated);
		Assert.Contains("horizontalQuality = \"HIGH\"", updated);
		Assert.Contains("maxTexturedLodDetailLevel = 2", updated);
		Assert.Contains("enableSsao = false", updated);
		Assert.DoesNotContain("verticalQuality = \"LOW\"", updated);
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-dh-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
