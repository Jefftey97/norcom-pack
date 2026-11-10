using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PackPathTests
{
	[Theory]
	[InlineData("mods/sodium.jar")]
	[InlineData("mods\\sodium.pw.toml")]
	[InlineData("config/norcom/client.json")]
	[InlineData("resourcepacks/NORCOM Marble.zip")]
	[InlineData("mods/auxiliary.jar")]
	[InlineData("mods/comet.jar")]
	public void PackPathsStayUnderTheGameFolder(string relative)
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-pack-path-" + Guid.NewGuid().ToString("N"));
		string full = PackSyncService.ConfinedPath(root, relative);
		Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
		Assert.True(PackSyncService.IsInsideDirectory(root, full));
	}

	[Theory]
	[InlineData("")]
	[InlineData("../outside.jar")]
	[InlineData("mods/../../outside.jar")]
	[InlineData("mods\\..\\..\\outside.jar")]
	[InlineData("mods/.. /../outside.jar")]
	[InlineData("mods/foo.")]
	[InlineData("mods/foo ")]
	[InlineData("/etc/outside.jar")]
	[InlineData("//server/share/outside.jar")]
	[InlineData("C:/Users/Public/outside.jar")]
	[InlineData("C:outside.jar")]
	[InlineData("\\\\server\\share\\outside.jar")]
	[InlineData("mods/NUL")]
	[InlineData("mods/nul.jar")]
	[InlineData("mods/AUX.txt")]
	[InlineData("mods/con")]
	[InlineData("mods/prn.log")]
	[InlineData("mods/COM1")]
	[InlineData("mods/lpt9.jar")]
	public void PackPathsCannotLeaveTheGameFolder(string relative)
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-pack-path-" + Guid.NewGuid().ToString("N"));
		Assert.Throws<InvalidDataException>(() => PackSyncService.ConfinedPath(root, relative));
	}

	[Fact]
	public void InsideCheckIsCaseInsensitiveAndRejectsThePrefixTrap()
	{
		string game = Path.Combine(Path.GetTempPath(), "NorcomGameFolder");
		string inside = Path.Combine(game, "mods", "Sodium.jar");
		Assert.True(PackSyncService.IsInsideDirectory(game, inside));
		string flipped = inside.Replace("NorcomGameFolder", "norcomgamefolder");
		Assert.NotEqual(inside, flipped);
		Assert.True(PackSyncService.IsInsideDirectory(game, flipped));
		string game2 = Path.Combine(Path.GetTempPath(), "NorcomGameFolder2");
		Assert.False(PackSyncService.IsInsideDirectory(game, Path.Combine(game2, "Sodium.jar")));
		Assert.False(PackSyncService.IsInsideDirectory(game, game));
		Assert.False(PackSyncService.IsInsideDirectory(game, Path.Combine(game, "..", "NorcomGameFolder2", "Sodium.jar")));
	}

	[Fact]
	public void DirectorySymlinkBelowTheGameRootIsRefused()
	{
		string parent = Path.Combine(Path.GetTempPath(), "norcom-pack-link-" + Guid.NewGuid().ToString("N"));
		string game = Path.Combine(parent, "game");
		string outside = Path.Combine(parent, "outside");
		Directory.CreateDirectory(game);
		Directory.CreateDirectory(outside);
		try
		{
			Directory.CreateSymbolicLink(Path.Combine(game, "mods"), outside);
			Assert.Throws<InvalidDataException>(() => PackSyncService.ConfinedPath(game, "mods/evil.jar"));
			Assert.Throws<InvalidDataException>(() => PackSyncService.ConfinedPath(game, "mods/nested/evil.jar"));
			Assert.False(Directory.Exists(Path.Combine(outside, "nested")));
			Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
		}
		finally
		{
			Directory.Delete(parent, recursive: true);
		}
	}

	[Fact]
	public void FileSymlinkAtTheDestinationIsRefused()
	{
		string parent = Path.Combine(Path.GetTempPath(), "norcom-pack-link-" + Guid.NewGuid().ToString("N"));
		string game = Path.Combine(parent, "game");
		string outside = Path.Combine(parent, "outside.txt");
		Directory.CreateDirectory(Path.Combine(game, "config"));
		File.WriteAllText(outside, "secret");
		string link = Path.Combine(game, "config", "keep.txt");
		File.CreateSymbolicLink(link, outside);
		try
		{
			Assert.Throws<InvalidDataException>(() => PackSyncService.ConfinedPath(game, "config/keep.txt"));
			Assert.Equal("secret", File.ReadAllText(outside));
			Assert.NotNull(File.ResolveLinkTarget(link, returnFinalTarget: false));
		}
		finally
		{
			Directory.Delete(parent, recursive: true);
		}
	}

	[Fact]
	public void GameRootMayItselfBeAJunction()
	{
		string parent = Path.Combine(Path.GetTempPath(), "norcom-pack-link-" + Guid.NewGuid().ToString("N"));
		string real = Path.Combine(parent, "real");
		string game = Path.Combine(parent, "game");
		Directory.CreateDirectory(real);
		Directory.CreateSymbolicLink(game, real);
		try
		{
			string full = PackSyncService.ConfinedPath(game, "config/norcom/client.json");
			Assert.True(PackSyncService.IsInsideDirectory(game, full));
			Assert.NotNull(Directory.ResolveLinkTarget(game, returnFinalTarget: false));
		}
		finally
		{
			Directory.Delete(parent, recursive: true);
		}
	}
}
