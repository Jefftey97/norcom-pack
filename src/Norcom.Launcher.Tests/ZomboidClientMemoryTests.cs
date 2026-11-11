using System.Text;
using Norcom.Launcher.Core.Games.Zomboid;
using Xunit;

namespace Norcom.Launcher.Tests;

public class ZomboidClientMemoryTests
{
	private const long Gib = 1024L * 1024L * 1024L;

	private const string Original =
		"{\r\n" +
		"\t\"mainClass\": \"zombie/gameStates/MainScreenState\",\r\n" +
		"\t\"classpath\": [\r\n" +
		"\t\t\".\",\r\n" +
		"\t\t\"projectzomboid.jar\"\r\n" +
		"\t],\r\n" +
		"\t\"other\": \"-Xmx3072m\",\r\n" +
		"\t\"vmArgs\": [\r\n" +
		"\t\t\"-Djava.awt.headless=true\",\r\n" +
		"\t\t\"-Xms512m\",\r\n" +
		"\t\t\"-Xmx3072m\",\r\n" +
		"\t\t\"-Dzomboid.steam=1\"\r\n" +
		"\t],\r\n" +
		"\t\"windows\": {\r\n" +
		"\t\t\"vmArgs\": [\r\n" +
		"\t\t\t\"-Xmx3072m\"\r\n" +
		"\t\t]\r\n" +
		"\t}\r\n" +
		"}\r\n";

	[Fact]
	public void EditRaises3072mTo8gAndLeavesEveryOtherByte()
	{
		ZomboidClientMemory.EditResult result = ZomboidClientMemory.Edit(Original, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.Replaced, result.Kind);
		Assert.Equal(WithHeap("8g"), result.Text);
		Assert.Contains("\"other\": \"-Xmx3072m\"", result.Text, StringComparison.Ordinal);
		Assert.Contains("\"-Xms512m\"", result.Text, StringComparison.Ordinal);
		Assert.Contains("\"-Djava.awt.headless=true\"", result.Text, StringComparison.Ordinal);
		const string unix = "{\"vmArgs\":[\"-Xmx3072m\"]}\n";
		Assert.Equal("{\"vmArgs\":[\"-Xmx8g\"]}\n", ZomboidClientMemory.Edit(unix, "8g").Text);
	}

	[Fact]
	public void EditUses6gWhenPhysicalRamIsAtMost16Gb()
	{
		string target = ZomboidClientMemory.HeapForTotalBytes(16L * Gib);
		Assert.Equal("6g", target);
		Assert.Equal("6g", ZomboidClientMemory.HeapForTotalBytes(16L * Gib - 1));
		Assert.Equal("6g", ZomboidClientMemory.HeapForTotalBytes(8L * Gib));
		ZomboidClientMemory.EditResult result = ZomboidClientMemory.Edit(Original, target);
		Assert.Equal(ZomboidClientMemory.EditKind.Replaced, result.Kind);
		Assert.Equal(WithHeap("6g"), result.Text);
	}

	[Fact]
	public void EditLeavesAnEqualOrHigherHeapAlone()
	{
		string higher = WithHeap("10g");
		ZomboidClientMemory.EditResult ten = ZomboidClientMemory.Edit(higher, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.AlreadySufficient, ten.Kind);
		Assert.Equal(higher, ten.Text);
		string equalMb = WithHeap("8192m");
		ZomboidClientMemory.EditResult same = ZomboidClientMemory.Edit(equalMb, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.AlreadySufficient, same.Kind);
		Assert.Equal(equalMb, same.Text);
		string eight = WithHeap("8g");
		ZomboidClientMemory.EditResult notLowered = ZomboidClientMemory.Edit(eight, "6g");
		Assert.Equal(ZomboidClientMemory.EditKind.AlreadySufficient, notLowered.Kind);
		Assert.Equal(eight, notLowered.Text);
		const string six = "{\"vmArgs\":[\"-Xmx6144m\"]}";
		Assert.Equal(six, ZomboidClientMemory.Edit(six, "6g").Text);
		Assert.Equal(six, ZomboidClientMemory.Edit(six, ZomboidClientMemory.HeapForTotalBytes(16L * Gib)).Text);
	}

	[Fact]
	public void EditMissingXmxLeavesTheTextUntouched()
	{
		string json = Original.Replace("\t\t\"-Xmx3072m\",\r\n", "\t\t\"-Djava.library.path=win64/;.\",\r\n", StringComparison.Ordinal).Replace("\t\t\t\"-Xmx3072m\"\r\n", "\t\t\t\"-Dzomboid.znetlog=1\"\r\n", StringComparison.Ordinal);
		ZomboidClientMemory.EditResult result = ZomboidClientMemory.Edit(json, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.MissingEntry, result.Kind);
		Assert.Equal(json, result.Text);
		const string noArray = "{\"classpath\":[\".\"]}";
		Assert.Equal(ZomboidClientMemory.EditKind.MissingEntry, ZomboidClientMemory.Edit(noArray, "8g").Kind);
		Assert.Equal(noArray, ZomboidClientMemory.Edit(noArray, "8g").Text);
	}

	[Fact]
	public void EditMalformedJsonLeavesTheTextUntouched()
	{
		const string trailing = "{ \"vmArgs\": [ \"-Xmx3072m\", ] }";
		ZomboidClientMemory.EditResult comma = ZomboidClientMemory.Edit(trailing, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.Malformed, comma.Kind);
		Assert.Equal(trailing, comma.Text);
		const string broken = "{ not json";
		ZomboidClientMemory.EditResult junk = ZomboidClientMemory.Edit(broken, "8g");
		Assert.Equal(ZomboidClientMemory.EditKind.Malformed, junk.Kind);
		Assert.Equal(broken, junk.Text);
		Assert.Equal(ZomboidClientMemory.EditKind.Malformed, ZomboidClientMemory.Edit("", "8g").Kind);
	}

	[Fact]
	public void HeapIs8gOnlyWhenPhysicalRamIsAbove16Gb()
	{
		Assert.Equal("8g", ZomboidClientMemory.HeapForTotalBytes(16L * Gib + 1));
		Assert.Equal("8g", ZomboidClientMemory.HeapForTotalBytes(32L * Gib));
		Assert.Equal("6g", ZomboidClientMemory.HeapForTotalBytes(16L * Gib));
		Assert.Equal("6g", ZomboidClientMemory.HeapForTotalBytes(0));
	}

	[Fact]
	public void ApplyRaises3072mTo8gAndKeepsAOneTimeBackup()
	{
		string dir = NewGameDir();
		string path = Path.Combine(dir, ZomboidClientMemory.FileName);
		string options = Path.Combine(dir, "options.ini");
		File.WriteAllText(options, "frameRate=144\n");
		byte[] optionsBefore = File.ReadAllBytes(options);
		byte[] body = Encoding.UTF8.GetBytes(Original);
		byte[] withBom = new byte[body.Length + 3];
		withBom[0] = 0xEF;
		withBom[1] = 0xBB;
		withBom[2] = 0xBF;
		Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
		File.WriteAllBytes(path, withBom);
		int elevated = 0;
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 32L * Gib, (_, _) =>
		{
			elevated++;
			return 1;
		});
		Assert.Equal(0, elevated);
		byte[] written = File.ReadAllBytes(path);
		Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written.Take(3).ToArray());
		Assert.Equal(WithHeap("8g"), Encoding.UTF8.GetString(written, 3, written.Length - 3));
		Assert.Equal(withBom, File.ReadAllBytes(Path.Combine(dir, ZomboidClientMemory.BackupName)));
		Assert.Equal(optionsBefore, File.ReadAllBytes(options));
		File.WriteAllText(Path.Combine(dir, ZomboidClientMemory.BackupName), "sentinel");
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 32L * Gib);
		Assert.Equal("sentinel", File.ReadAllText(Path.Combine(dir, ZomboidClientMemory.BackupName)));
		Assert.Equal(WithHeap("8g"), Encoding.UTF8.GetString(File.ReadAllBytes(path), 3, File.ReadAllBytes(path).Length - 3));
		Assert.False(File.Exists(path + ".norcom-new"));
	}

	[Fact]
	public void ApplyUses6gAt16GbAndDoesNotLowerAHigherHeap()
	{
		string dir = NewGameDir();
		string path = Path.Combine(dir, ZomboidClientMemory.FileName);
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Original));
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 16L * Gib);
		Assert.Equal(WithHeap("6g"), File.ReadAllText(path));
		Assert.Equal(Original, File.ReadAllText(Path.Combine(dir, ZomboidClientMemory.BackupName)));
		string higher = WithHeap("8g");
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(higher));
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 8L * Gib);
		Assert.Equal(higher, File.ReadAllText(path));
		Assert.Equal(Original, File.ReadAllText(Path.Combine(dir, ZomboidClientMemory.BackupName)));
	}

	[Fact]
	public void ApplyLeavesMissingEntryAndMalformedJsonUntouched()
	{
		string missing = Original.Replace("\t\t\"-Xmx3072m\",\r\n", "\t\t\"-Dzomboid.znetlog=1\",\r\n", StringComparison.Ordinal).Replace("\t\t\t\"-Xmx3072m\"\r\n", "\t\t\t\"-Dzomboid.steam=1\"\r\n", StringComparison.Ordinal);
		string missingDir = NewGameDir();
		string missingPath = Path.Combine(missingDir, ZomboidClientMemory.FileName);
		File.WriteAllBytes(missingPath, Encoding.UTF8.GetBytes(missing));
		ZomboidClientMemory.ApplyBeforeLaunch(missingDir, 32L * Gib);
		Assert.Equal(missing, File.ReadAllText(missingPath));
		Assert.False(File.Exists(Path.Combine(missingDir, ZomboidClientMemory.BackupName)));

		string badDir = NewGameDir();
		string badPath = Path.Combine(badDir, ZomboidClientMemory.FileName);
		byte[] bad = Encoding.UTF8.GetBytes("{ \"vmArgs\": [\"-Xmx3072m\" ");
		File.WriteAllBytes(badPath, bad);
		ZomboidClientMemory.ApplyBeforeLaunch(badDir, 32L * Gib);
		Assert.Equal(bad, File.ReadAllBytes(badPath));
		Assert.False(File.Exists(Path.Combine(badDir, ZomboidClientMemory.BackupName)));

		string absent = NewGameDir();
		ZomboidClientMemory.ApplyBeforeLaunch(absent, 32L * Gib);
		Assert.False(File.Exists(Path.Combine(absent, ZomboidClientMemory.FileName)));
		Assert.False(File.Exists(Path.Combine(absent, ZomboidClientMemory.BackupName)));
	}

	[Fact]
	public void AccessDeniedElevatesOnceAndADeclineLeavesTheFile()
	{
		string dir = NewGameDir();
		string path = Path.Combine(dir, ZomboidClientMemory.FileName);
		byte[] before = Encoding.UTF8.GetBytes(Original);
		File.WriteAllBytes(path, before);
		int calls = 0;
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 32L * Gib, (jsonPath, heap) =>
		{
			calls++;
			Assert.Equal(path, jsonPath);
			Assert.Equal("8g", heap);
			return ZomboidClientMemory.ExitDeclined;
		}, (_, _, _) => throw new UnauthorizedAccessException("denied"));
		Assert.Equal(1, calls);
		Assert.Equal(before, File.ReadAllBytes(path));
		Assert.False(File.Exists(Path.Combine(dir, ZomboidClientMemory.BackupName)));
	}

	[Fact]
	public void LockedFileDoesNotElevateAndPlayCanContinue()
	{
		string dir = NewGameDir();
		string path = Path.Combine(dir, ZomboidClientMemory.FileName);
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Original));
		int calls = 0;
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 32L * Gib, (_, _) =>
		{
			calls++;
			return 1;
		}, (_, _, _) => throw new IOException("locked", unchecked((int)0x80070020)));
		Assert.Equal(0, calls);
		Assert.Equal(Original, File.ReadAllText(path));
	}

	[Fact]
	public void ElevatedHelperWritesTheHeapWhenDirectWriteIsDenied()
	{
		string dir = NewGameDir();
		string path = Path.Combine(dir, ZomboidClientMemory.FileName);
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(Original));
		int calls = 0;
		ZomboidClientMemory.ApplyBeforeLaunch(dir, 32L * Gib, (jsonPath, heap) =>
		{
			calls++;
			return ZomboidClientMemory.RunElevated(new[] { ZomboidClientMemory.ArgName, jsonPath, heap });
		}, (_, _, _) => throw new UnauthorizedAccessException("denied"));
		Assert.Equal(1, calls);
		Assert.Equal(WithHeap("8g"), File.ReadAllText(path));
		Assert.Equal(Original, File.ReadAllText(Path.Combine(dir, ZomboidClientMemory.BackupName)));
	}

	[Fact]
	public void ElevatedRequestRejectsAnythingButTheGameJson()
	{
		Assert.False(ZomboidClientMemory.IsElevatedRequest(Array.Empty<string>()));
		Assert.False(ZomboidClientMemory.IsElevatedRequest(new[] { "--other" }));
		Assert.True(ZomboidClientMemory.IsElevatedRequest(new[] { ZomboidClientMemory.ArgName, "x", "8g" }));
		Assert.Equal(ZomboidClientMemory.ExitFailed, ZomboidClientMemory.RunElevated(new[] { ZomboidClientMemory.ArgName }));
		string outsider = Path.Combine(Path.GetTempPath(), "norcom-not-zomboid-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(outsider);
		string outsiderJson = Path.Combine(outsider, ZomboidClientMemory.FileName);
		File.WriteAllText(outsiderJson, Original);
		Assert.Equal(ZomboidClientMemory.ExitFailed, ZomboidClientMemory.RunElevated(new[] { ZomboidClientMemory.ArgName, outsiderJson, "8g" }));
		Assert.Equal(Original, File.ReadAllText(outsiderJson));
		string gameJson = Path.Combine(NewGameDir(), ZomboidClientMemory.FileName);
		File.WriteAllText(gameJson, Original);
		Assert.Equal(ZomboidClientMemory.ExitFailed, ZomboidClientMemory.RunElevated(new[] { ZomboidClientMemory.ArgName, gameJson, "4g" }));
		Assert.Equal(Original, File.ReadAllText(gameJson));
	}

	private static string WithHeap(string heap)
	{
		return Original.Replace("\t\t\"-Xmx3072m\",\r\n", "\t\t\"-Xmx" + heap + "\",\r\n", StringComparison.Ordinal).Replace("\t\t\t\"-Xmx3072m\"\r\n", "\t\t\t\"-Xmx" + heap + "\"\r\n", StringComparison.Ordinal);
	}

	private static string NewGameDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-zomboid-xmx-" + Guid.NewGuid().ToString("n"), "ProjectZomboid");
		Directory.CreateDirectory(dir);
		return dir;
	}
}
