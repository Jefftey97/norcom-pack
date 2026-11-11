using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class TexturePackTests
{
	[Fact]
	public void MissingChoiceDefaultsToAlacrityAndPatrixWinsWhenItIsEnabled()
	{
		Assert.Equal(TexturePackChoice.Alacrity, TexturePackChoice.Detect(""));
		Assert.Equal(TexturePackChoice.Alacrity, TexturePackChoice.Detect("[\"vanilla\",\"file/Alacrity.zip\"]"));
		Assert.Equal(TexturePackChoice.Patrix, TexturePackChoice.Detect("[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\"]"));
	}

	[Fact]
	public void LaunchAndResyncLeaveEditedOptionsAndShadersUntouched()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "shaderpacks"));
		string options = "renderDistance:11\nmaxFps:90\nresourcePacks:[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\",\"file/norcom_marble.zip\"]\nkey_key.attack:key.mouse.left\n";
		string shader = "profile=CUSTOM\nRP_MODE=3\nshadowDistance=12.0\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		File.WriteAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), shader);
		for (int i = 0; i < 2; i++)
		{
			GraphicsPlay.ApplyAuto(dir);
			GameLaunchGraphics.ApplyBeforeLaunch("minecraft", dir, NewDir(), "", NewDir());
			TexturePackChoice.PrepareForLaunch(dir, "", saved: false);
		}
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal(shader, File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt")));
	}

	[Fact]
	public void ChoosingPatrixChangesOnlyThePackLinesAndRestoresHeldZips()
	{
		string dir = NewDir();
		string packs = Path.Combine(dir, "resourcepacks");
		string held = Path.Combine(packs, "norcom-held");
		Directory.CreateDirectory(held);
		File.WriteAllText(Path.Combine(held, "Patrix_26.3_32x_basic.zip"), "basic");
		File.WriteAllText(Path.Combine(held, "Patrix_26.3_32x_addon.zip"), "addon");
		File.WriteAllText(Path.Combine(held, "Patrix_26.3_models.zip"), "models");
		File.WriteAllText(Path.Combine(packs, "norcom_marble.zip"), "marble");
		string options = "renderDistance:15\nresourcePacks:[\"vanilla\",\"continuity:default\",\"file/Alacrity.zip\",\"file/norcom_marble.zip\"]\nincompatibleResourcePacks:[\"file/Patrix_26.3_models.zip\"]\nkey_key.attack:key.mouse.left\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		Directory.CreateDirectory(Path.Combine(dir, "config"));
		File.WriteAllText(Path.Combine(dir, "config", "iris.properties"), "shaderPack=ComplementaryUnbound_r5.9.3.zip\nmaxShadowRenderDistance=8\n");
		Assert.True(TexturePackChoice.ApplyListIfDifferent(dir, TexturePackChoice.Patrix));
		TexturePackChoice.BringFilesToTop(dir, TexturePackChoice.Patrix);
		string updated = File.ReadAllText(Path.Combine(dir, "options.txt"));
		Assert.Contains("renderDistance:15", updated);
		Assert.Contains("key_key.attack:key.mouse.left", updated);
		Assert.Contains("file/norcom_marble.zip", updated);
		Assert.DoesNotContain("file/Alacrity.zip", updated);
		string list = Read(updated, "resourcePacks");
		int continuity = list.IndexOf("continuity:default", StringComparison.Ordinal);
		int basic = list.IndexOf("file/Patrix_26.3_32x_basic.zip", StringComparison.Ordinal);
		int addon = list.IndexOf("file/Patrix_26.3_32x_addon.zip", StringComparison.Ordinal);
		int models = list.IndexOf("file/Patrix_26.3_models.zip", StringComparison.Ordinal);
		int marble = list.IndexOf("file/norcom_marble.zip", StringComparison.Ordinal);
		Assert.True(continuity >= 0 && continuity < basic && basic < addon && addon < models && models < marble);
		Assert.DoesNotContain("Patrix_26.3_models", Read(updated, "incompatibleResourcePacks"));
		Assert.Equal("basic", File.ReadAllText(Path.Combine(packs, "Patrix_26.3_32x_basic.zip")));
		Assert.Equal("addon", File.ReadAllText(Path.Combine(packs, "Patrix_26.3_32x_addon.zip")));
		Assert.Equal("models", File.ReadAllText(Path.Combine(packs, "Patrix_26.3_models.zip")));
		Assert.False(File.Exists(Path.Combine(held, "Patrix_26.3_32x_basic.zip")));
		Assert.Equal("marble", File.ReadAllText(Path.Combine(packs, "norcom_marble.zip")));
		Assert.False(TexturePackChoice.ApplyListIfDifferent(dir, TexturePackChoice.Patrix));
		Assert.Equal(updated, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal(TexturePackChoice.LabPbrHint, TexturePackChoice.Hint(dir, TexturePackChoice.Patrix));
		Assert.Equal("shaderPack=ComplementaryUnbound_r5.9.3.zip\nmaxShadowRenderDistance=8\n", File.ReadAllText(Path.Combine(dir, "config", "iris.properties")));
	}

	private static string Read(string options, string key)
	{
		foreach (string line in options.Split('\n'))
		{
			if (line.StartsWith(key + ":", StringComparison.Ordinal))
			{
				return line.Substring(key.Length + 1);
			}
		}
		return "";
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-packs-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
