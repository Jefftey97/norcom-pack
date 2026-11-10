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
}
