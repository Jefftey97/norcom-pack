using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ZomboidTexCacheTests
{
	private const long Plenty = 500L * 1024 * 1024 * 1024;

	[Fact]
	public void NoFileGetsTheLargerLimit()
	{
		string dir = NewDir();
		string note = ZomboidTexCache.Apply(dir, _ => Plenty);
		Assert.Equal("mb=8192\r\n", File.ReadAllText(Path.Combine(dir, ZomboidTexCache.FileName)));
		Assert.Contains("8 GB", note, StringComparison.Ordinal);
	}

	[Fact]
	public void SmallLimitIsRaisedAndOtherLinesKept()
	{
		string dir = NewDir();
		string path = Path.Combine(dir, ZomboidTexCache.FileName);
		File.WriteAllText(path, "# mine\nmode=on\nmb=2048\nextra=1\n");
		ZomboidTexCache.Apply(dir, _ => Plenty);
		Assert.Equal(new[] { "# mine", "mode=on", "mb=8192", "extra=1" }, File.ReadAllLines(path));
	}

	[Fact]
	public void TurnedOffCacheIsLeftAlone()
	{
		string dir = NewDir();
		string path = Path.Combine(dir, ZomboidTexCache.FileName);
		File.WriteAllText(path, "mode=off\nmb=1024\n");
		Assert.Equal("", ZomboidTexCache.Apply(dir, _ => Plenty));
		Assert.Equal("mode=off\nmb=1024\n", File.ReadAllText(path));
	}

	[Fact]
	public void LargerLimitIsNeverLowered()
	{
		string dir = NewDir();
		string path = Path.Combine(dir, ZomboidTexCache.FileName);
		File.WriteAllText(path, "mb=16384\n");
		Assert.Equal("", ZomboidTexCache.Apply(dir, _ => Plenty));
		Assert.Equal("mb=16384\n", File.ReadAllText(path));
	}

	[Fact]
	public void LowDiskSpaceChangesNothing()
	{
		string dir = NewDir();
		long tooLittle = ZomboidTexCache.MarginBytes + 1024L * 1024 * 1024;
		Assert.Equal("", ZomboidTexCache.Apply(dir, _ => tooLittle));
		Assert.False(File.Exists(Path.Combine(dir, ZomboidTexCache.FileName)));
	}

	[Fact]
	public void UnreadableDiskChangesNothing()
	{
		string dir = NewDir();
		Assert.Equal("", ZomboidTexCache.Apply(dir, _ => throw new IOException("no drive")));
		Assert.False(File.Exists(Path.Combine(dir, ZomboidTexCache.FileName)));
	}

	[Fact]
	public void SecondRunChangesNothing()
	{
		string dir = NewDir();
		ZomboidTexCache.Apply(dir, _ => Plenty);
		string path = Path.Combine(dir, ZomboidTexCache.FileName);
		DateTime first = File.GetLastWriteTimeUtc(path);
		Assert.Equal("", ZomboidTexCache.Apply(dir, _ => Plenty));
		Assert.Equal(first, File.GetLastWriteTimeUtc(path));
		Assert.False(File.Exists(path + ".norcom-tmp"));
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-texcache-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
