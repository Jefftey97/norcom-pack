using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class RamBudgetTests
{
	private const long Gib = 1024L * 1024L * 1024L;

	[Fact]
	public void SafeMaxKeepsTheLargerOfAQuarterOrFourGigabytes()
	{
		RamBudget sixteen = RamBudget.Create(16L * Gib, 10L * Gib, 0);
		Assert.Equal(16384, sixteen.TotalMb);
		Assert.Equal(4096, sixteen.ReserveMb);
		Assert.Equal(12288, sixteen.SafeMaxMb);
		Assert.Equal(6144, sixteen.RecommendedMb);
		Assert.Equal(6144, sixteen.ChosenMb);
		RamBudget eight = RamBudget.Create(8L * Gib, 6L * Gib, 5632);
		Assert.Equal(4096, eight.SafeMaxMb);
		Assert.Equal(4096, eight.ChosenMb);
		RamBudget thirtyTwo = RamBudget.Create(32L * Gib, 20L * Gib, 12288);
		Assert.Equal(8192, thirtyTwo.ReserveMb);
		Assert.Equal(24576, thirtyTwo.SafeMaxMb);
		Assert.Equal(12288, thirtyTwo.ChosenMb);
	}

	[Fact]
	public void ChoiceAboveFreeMemoryIsWarnedAndNotRaisedPastTheSafeMax()
	{
		RamBudget budget = RamBudget.Create(16L * Gib, 2L * Gib, 20000);
		Assert.Equal(12288, budget.ChosenMb);
		Assert.Contains("free", budget.Warning, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(12288, MemoryPlan.ForPlay(20000, 16L * Gib, "NVIDIA GeForce RTX 3070"));
	}

	[Fact]
	public void MeminfoAndCpuinfoParseLocally()
	{
		(long total, long available) = HostMemory.ParseMeminfo("MemTotal:       16384000 kB\nMemFree: 100 kB\nMemAvailable:   8192000 kB\n");
		Assert.Equal(16384000L * 1024L, total);
		Assert.Equal(8192000L * 1024L, available);
		CpuInfo cpu = HostCpu.ParseCpuinfo("processor\t: 0\nmodel name\t: Test CPU\ncpu MHz\t\t: 2400.5\ncpu cores\t: 4\nprocessor\t: 1\n");
		Assert.Equal("Test CPU", cpu.Name);
		Assert.Equal(4, cpu.Cores);
		Assert.Equal(2, cpu.Threads);
		Assert.Equal(2400, cpu.Mhz);
	}

	[Fact]
	public void ZomboidHeapOverrideBacksUpAndLeavesTheInstallAlone()
	{
		string user = Path.Combine(Path.GetTempPath(), "norcom-ram-" + Guid.NewGuid().ToString("n"));
		string install = Path.Combine(Path.GetTempPath(), "norcom-ram-install-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(install);
		string original = "{\"mainClass\":\"zombie/gameStates/MainScreenState\",\"vmArgs\":[\"-Djava.awt.headless=true\",\"-Xmx3072m\"],\"windows\":{\"vmArgs\":[\"-Xmx3072m\"]}}";
		string installFile = Path.Combine(install, ZomboidMemory.FileName);
		File.WriteAllText(installFile, original);
		string userRoot = Path.Combine(user, "Zomboid");
		string written = ZomboidMemory.Apply(userRoot, install, 8192);
		Assert.Equal(original, File.ReadAllText(installFile));
		Assert.Equal(original, File.ReadAllText(Path.Combine(userRoot, ZomboidMemory.BackupName)));
		string userJson = File.ReadAllText(written);
		Assert.Contains("\"-Xmx8192m\"", userJson, StringComparison.Ordinal);
		Assert.Contains("\"-Xms8192m\"", userJson, StringComparison.Ordinal);
		Assert.Contains("-Djava.awt.headless=true", userJson, StringComparison.Ordinal);
		ZomboidMemory.Apply(userRoot, install, 4096);
		Assert.Equal(original, File.ReadAllText(Path.Combine(userRoot, ZomboidMemory.BackupName)));
		string updated = File.ReadAllText(written);
		Assert.Contains("\"-Xmx4096m\"", updated, StringComparison.Ordinal);
		Assert.DoesNotContain("8192", updated, StringComparison.Ordinal);
		Assert.Contains("-Xmx4096m", ZomboidMemory.WithHeap("steam://rungameid/108600//+connect%20play.example.com:16261", 4096), StringComparison.Ordinal);
	}
}
