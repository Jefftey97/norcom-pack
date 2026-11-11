using Norcom.Launcher.Core.Games.Minecraft;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class TexturePackTests
{
	[Fact]
	public void ChoosingAlacrityReplacesPatrixAndStaysSelectedAfterReload()
	{
		string home = NewDir();
		string game = NewDir();
		string previousHome = Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
		string previousGame = Environment.GetEnvironmentVariable("NORCOM_GAME_DIR");
		Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", home);
		Environment.SetEnvironmentVariable("NORCOM_GAME_DIR", game);
		try
		{
			string options = "renderDistance:12\nresourcePacks:[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\"]\nkey_key.attack:key.mouse.left\n";
			File.WriteAllText(Path.Combine(game, "options.txt"), options);
			Directory.CreateDirectory(Path.Combine(game, "config"));
			File.WriteAllText(Path.Combine(game, "config", "iris.properties"), "shaderPack=ComplementaryUnbound_r5.9.3.zip\n");
			MinecraftGame first = new MinecraftGame();
			Assert.Equal(TexturePackChoice.Patrix, first.TexturePackName());
			first.ChooseTexture(TexturePackChoice.Alacrity);
			Assert.Equal(TexturePackChoice.Alacrity, first.TexturePackName());
			string updated = File.ReadAllText(Path.Combine(game, "options.txt"));
			Assert.Contains("file/Alacrity.zip", updated, StringComparison.Ordinal);
			Assert.DoesNotContain("Patrix", updated, StringComparison.Ordinal);
			Assert.Contains("renderDistance:12", updated, StringComparison.Ordinal);
			Assert.Contains("key_key.attack:key.mouse.left", updated, StringComparison.Ordinal);
			Assert.Equal("shaderPack=ComplementaryUnbound_r5.9.3.zip\n", File.ReadAllText(Path.Combine(game, "config", "iris.properties")));
			MinecraftState saved = MinecraftState.Load(home);
			Assert.Equal(TexturePackChoice.Alacrity, saved.TexturePack);
			Assert.True(saved.TexturePackSpecified);
			Assert.False(saved.TexturePackPending);
			Assert.Equal(TexturePackChoice.Alacrity, new MinecraftGame().TexturePackName());
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", previousHome);
			Environment.SetEnvironmentVariable("NORCOM_GAME_DIR", previousGame);
		}
	}

	[Fact]
	public void ChoosingPatrixFromAlacrityWritesPatrixAndReloads()
	{
		string home = NewDir();
		string game = NewDir();
		string previousHome = Environment.GetEnvironmentVariable("NORCOM_STATE_DIR");
		string previousGame = Environment.GetEnvironmentVariable("NORCOM_GAME_DIR");
		Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", home);
		Environment.SetEnvironmentVariable("NORCOM_GAME_DIR", game);
		try
		{
			string options = "renderDistance:12\nresourcePacks:[\"vanilla\",\"file/Alacrity.zip\"]\nincompatibleResourcePacks:[\"file/Patrix_26.3_models.zip\"]\nkey_key.attack:key.mouse.left\n";
			File.WriteAllText(Path.Combine(game, "options.txt"), options);
			Directory.CreateDirectory(Path.Combine(game, "config"));
			File.WriteAllText(Path.Combine(game, "config", "iris.properties"), "shaderPack=ComplementaryUnbound_r5.9.3.zip\n");
			MinecraftState pending = new MinecraftState
			{
				RamMb = 4096,
				TexturePack = TexturePackChoice.Alacrity,
				TexturePackSpecified = true,
				TexturePackPending = true
			};
			pending.Save(home);
			MinecraftGame first = new MinecraftGame();
			Assert.Equal(TexturePackChoice.Alacrity, first.TexturePackName());
			first.ChooseTexture(TexturePackChoice.Patrix);
			string updated = File.ReadAllText(Path.Combine(game, "options.txt"));
			Assert.Contains("file/Patrix_26.3_32x_basic.zip", updated, StringComparison.Ordinal);
			Assert.Contains("file/Patrix_26.3_32x_addon.zip", updated, StringComparison.Ordinal);
			Assert.Contains("file/Patrix_26.3_models.zip", updated, StringComparison.Ordinal);
			Assert.DoesNotContain("file/Alacrity.zip", updated, StringComparison.Ordinal);
			Assert.Contains("renderDistance:12", updated, StringComparison.Ordinal);
			Assert.Contains("key_key.attack:key.mouse.left", updated, StringComparison.Ordinal);
			Assert.DoesNotContain("Patrix_26.3_models", Read(updated, "incompatibleResourcePacks"), StringComparison.Ordinal);
			Assert.Equal("shaderPack=ComplementaryUnbound_r5.9.3.zip\n", File.ReadAllText(Path.Combine(game, "config", "iris.properties")));
			MinecraftState saved = MinecraftState.Load(home);
			Assert.Equal(TexturePackChoice.Patrix, saved.TexturePack);
			Assert.True(saved.TexturePackSpecified);
			Assert.False(saved.TexturePackPending);
			Assert.Equal(4096, saved.RamMb);
			Assert.Equal(TexturePackChoice.Patrix, new MinecraftGame().TexturePackName());
		}
		finally
		{
			Environment.SetEnvironmentVariable("NORCOM_STATE_DIR", previousHome);
			Environment.SetEnvironmentVariable("NORCOM_GAME_DIR", previousGame);
		}
	}

	[Fact]
	public void MissingChoiceDefaultsToAlacrityAndPatrixWinsWhenItIsEnabled()
	{
		Assert.Equal(TexturePackChoice.Alacrity, TexturePackChoice.Detect(""));
		Assert.Equal(TexturePackChoice.Alacrity, TexturePackChoice.Detect("[\"vanilla\",\"file/Alacrity.zip\"]"));
		Assert.Equal(TexturePackChoice.Patrix, TexturePackChoice.Detect("[\"vanilla\",\"continuity:default\",\"file/Patrix_26.3_32x_basic.zip\"]"));
	}

	[Theory]
	[InlineData(TexturePackChoice.Patrix)]
	[InlineData(TexturePackChoice.Alacrity)]
	public async Task PlayAndResyncLeaveAnEditedGameUntouched(string savedChoice)
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "shaderpacks"));
		Directory.CreateDirectory(Path.Combine(dir, "config"));
		string options = "renderDistance:11\nmaxFps:90\nresourcePacks:[\"vanilla\",\"continuity:default\",\"file/SomeOtherPack.zip\",\"file/norcom_marble.zip\"]\nkey_key.attack:key.mouse.left\n";
		string shader = "profile=CUSTOM\nRP_MODE=3\nshadowDistance=12.0\n";
		string iris = "shaderPack=ComplementaryUnbound_r5.9.3.zip\nmaxShadowRenderDistance=3\n";
		string horizons = "# edited\nlodChunkRenderDistanceRadius = 40\n[client.advanced.graphics.quality]\nhorizontalQuality = \"LOW\"\n";
		File.WriteAllText(Path.Combine(dir, "options.txt"), options);
		File.WriteAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), shader);
		File.WriteAllText(Path.Combine(dir, "config", "iris.properties"), iris);
		File.WriteAllText(Path.Combine(dir, "config", "DistantHorizons.toml"), horizons);
		for (int i = 0; i < 2; i++)
		{
			await Resync(dir);
			Play(dir, savedChoice, pending: false);
		}
		Assert.Equal(options, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal(shader, File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt")));
		Assert.Equal(iris, File.ReadAllText(Path.Combine(dir, "config", "iris.properties")));
		Assert.Equal(horizons, File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml")));
	}

	[Fact]
	public async Task PickerAppliesOnceAndTheNextPlayDoesNot()
	{
		string dir = NewDir();
		Directory.CreateDirectory(Path.Combine(dir, "shaderpacks"));
		Directory.CreateDirectory(Path.Combine(dir, "config"));
		File.WriteAllText(Path.Combine(dir, "options.txt"), "renderDistance:15\nresourcePacks:[\"vanilla\",\"file/Alacrity.zip\"]\nkey_key.attack:key.mouse.left\n");
		File.WriteAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt"), "profile=CUSTOM\n");
		File.WriteAllText(Path.Combine(dir, "config", "iris.properties"), "shaderPack=ComplementaryUnbound_r5.9.3.zip\n");
		File.WriteAllText(Path.Combine(dir, "config", "DistantHorizons.toml"), "lodChunkRenderDistanceRadius = 20\n");
		Assert.True(TexturePackChoice.ApplyPicker(dir, TexturePackChoice.Patrix));
		string applied = File.ReadAllText(Path.Combine(dir, "options.txt"));
		Assert.Contains("file/Patrix_26.3_models.zip", applied);
		string edited = applied.Replace("renderDistance:15", "renderDistance:4", StringComparison.Ordinal).Replace("file/Patrix_26.3_models.zip", "file/InGamePack.zip", StringComparison.Ordinal);
		File.WriteAllText(Path.Combine(dir, "options.txt"), edited);
		await Resync(dir);
		Play(dir, TexturePackChoice.Patrix, pending: false);
		Play(dir, TexturePackChoice.Patrix, pending: false);
		Assert.Equal(edited, File.ReadAllText(Path.Combine(dir, "options.txt")));
		Assert.Equal("profile=CUSTOM\n", File.ReadAllText(Path.Combine(dir, "shaderpacks", "ComplementaryUnbound_r5.9.3.txt")));
		Assert.Equal("shaderPack=ComplementaryUnbound_r5.9.3.zip\n", File.ReadAllText(Path.Combine(dir, "config", "iris.properties")));
		Assert.Equal("lodChunkRenderDistanceRadius = 20\n", File.ReadAllText(Path.Combine(dir, "config", "DistantHorizons.toml")));
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

	private static void Play(string dir, string savedChoice, bool pending)
	{
		TexturePackChoice.PrepareForLaunch(dir, savedChoice, saved: true, pending);
		DistantHorizonsDefaults.EnsureFresh(dir, GraphicsTier.High);
		GameLaunchGraphics.ApplyBeforeLaunch("minecraft", dir, NewDir(), "", NewDir());
		GraphicsPlay.ApplyAuto(dir);
		PrismPlay.Prepare(null, Array.Empty<string>(), NewDir(), dir, "java", 4096);
	}

	private static async Task Resync(string dir)
	{
		string root = NewDir();
		byte[] index = System.Text.Encoding.UTF8.GetBytes("hash-format = \"sha256\"\n");
		string indexHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(index)).ToLowerInvariant();
		string pack = "name = \"NORCOM\"\nversion = \"1\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + indexHash + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
		using HttpClient http = new HttpClient(new PackHandler(pack, index));
		await new PackSyncService(http).SyncAsync(dir, Path.Combine(root, "managed.json"), Path.Combine(root, "cache"), "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
	}

	private sealed class PackHandler : HttpMessageHandler
	{
		private readonly byte[] _pack;
		private readonly byte[] _index;

		public PackHandler(string pack, byte[] index)
		{
			_pack = System.Text.Encoding.UTF8.GetBytes(pack);
			_index = index;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.AbsoluteUri ?? "";
			if (url.EndsWith("pack.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(_pack) });
			}
			if (url.EndsWith("index.toml", StringComparison.Ordinal))
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(_index) });
			}
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
		}
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
