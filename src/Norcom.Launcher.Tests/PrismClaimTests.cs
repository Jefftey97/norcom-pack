using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PrismClaimTests
{
	[Fact]
	public void ExistingNorcomInstanceIsClaimedWithoutWipingSaves()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-prism-" + Guid.NewGuid().ToString("n"));
		string data = Path.Combine(root, "Prism");
		string instance = Path.Combine(data, "instances", "NORCOM");
		string minecraft = Path.Combine(instance, "minecraft");
		string game = Path.Combine(root, ".norcom");
		Directory.CreateDirectory(Path.Combine(minecraft, "saves", "BuddyWorld"));
		File.WriteAllText(Path.Combine(minecraft, "saves", "BuddyWorld", "level.dat"), "prism-world");
		File.WriteAllText(Path.Combine(minecraft, "options.txt"), "fov:1");
		Directory.CreateDirectory(Path.Combine(game, "saves", "BuddyWorld"));
		File.WriteAllText(Path.Combine(game, "saves", "BuddyWorld", "level.dat"), "norcom-world");
		Directory.CreateDirectory(Path.Combine(minecraft, "saves", "ExtraWorld"));
		File.WriteAllText(Path.Combine(minecraft, "saves", "ExtraWorld", "level.dat"), "extra-world");
		File.WriteAllText(Path.Combine(instance, "instance.cfg"), "[General]\r\nname=Old\r\niconKey=keep-me\r\n");
		Assert.False(File.Exists(Path.Combine(instance, PrismInstance.MarkerName)));

		Exception? error = Record.Exception(() => PrismInstance.Ensure(data, game, "C:\\java\\bin\\java.exe", 4096));

		Assert.Null(error);
		Assert.Equal("NORCOM", File.ReadAllText(Path.Combine(instance, PrismInstance.MarkerName)).Trim());
		Assert.Equal("norcom-world", File.ReadAllText(Path.Combine(game, "saves", "BuddyWorld", "level.dat")));
		Assert.Equal("extra-world", File.ReadAllText(Path.Combine(game, "saves", "ExtraWorld", "level.dat")));
		Assert.Equal("fov:1", File.ReadAllText(Path.Combine(game, "options.txt")));
		string kept = Path.Combine(instance, "minecraft.norcom-kept");
		Assert.True(Directory.Exists(kept));
		Assert.Equal("prism-world", File.ReadAllText(Path.Combine(kept, "saves", "BuddyWorld", "level.dat")));
		Assert.Equal("extra-world", File.ReadAllText(Path.Combine(kept, "saves", "ExtraWorld", "level.dat")));
		string cfg = File.ReadAllText(Path.Combine(instance, "instance.cfg"));
		Assert.Contains("iconKey=keep-me", cfg);
		Assert.Contains("JavaPath=C:/java/bin/java.exe", cfg);
		FileSystemInfo? link = Directory.ResolveLinkTarget(Path.Combine(instance, "minecraft"), returnFinalTarget: true);
		Assert.NotNull(link);
		Assert.Equal(Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar), link!.FullName.TrimEnd(Path.DirectorySeparatorChar));

		Exception? again = Record.Exception(() => PrismInstance.Ensure(data, game, "C:\\java\\bin\\java.exe", 4096));
		Assert.Null(again);
		Assert.Equal("prism-world", File.ReadAllText(Path.Combine(kept, "saves", "BuddyWorld", "level.dat")));
		Assert.Equal("norcom-world", File.ReadAllText(Path.Combine(game, "saves", "BuddyWorld", "level.dat")));
	}

	[Fact]
	public void OverlappingGameFolderIsStillRefused()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-prism-overlap-" + Guid.NewGuid().ToString("n"));
		string data = Path.Combine(root, "Prism");
		string instance = Path.Combine(data, "instances", "NORCOM");
		string game = Path.Combine(instance, ".norcom");
		Directory.CreateDirectory(instance);
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => PrismInstance.Ensure(data, game, "java", 4096));
		Assert.Contains("overlap", ex.Message, StringComparison.OrdinalIgnoreCase);
		Assert.False(File.Exists(Path.Combine(instance, PrismInstance.MarkerName)));
	}
}
