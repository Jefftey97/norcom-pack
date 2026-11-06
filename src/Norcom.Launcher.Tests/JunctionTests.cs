using Xunit;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Tests;

public class JunctionTests
{
	[Theory]
	[InlineData(@"C:\Users\Jeffrey\AppData\Roaming\.norcom")]
	[InlineData("/home/player/.norcom")]
	public void NorcomGameFolderIsAccepted(string path)
	{
		Assert.True(PrismInstance.EndsWithNorcomGame(path));
	}

	[Theory]
	[InlineData(@"C:\Games\Minecraft")]
	[InlineData("/tmp/instance/minecraft")]
	[InlineData(@"D:\.norcom-backup")]
	public void OtherTargetsAreRefused(string path)
	{
		Assert.False(PrismInstance.EndsWithNorcomGame(path));
	}

	[Fact]
	public void PlayRefusesAGameFolderThatIsNotNorcom()
	{
		string link = Path.Combine(Path.GetTempPath(), "norcom-link-" + Guid.NewGuid().ToString("n"));
		string game = Path.Combine(Path.GetTempPath(), "not-the-game-" + Guid.NewGuid().ToString("n"));
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => PrismInstance.EnsureGameLink(link, game));
		Assert.Contains(".norcom", ex.Message);
		Assert.False(Directory.Exists(game));
	}
}
