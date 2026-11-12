using Norcom.Launcher.Core.Games.Minecraft;
using Norcom.Launcher.Core.Games.Zomboid;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class RamEveryPlayTests
{
	private const long Gib = 1024L * 1024L * 1024L;

	[Fact]
	public void ZomboidSliderValueIsUsedOnEveryPlayNotOnlyThePendingOne()
	{
		ZomboidState moved = new ZomboidState { RamMb = 17408, RamSpecified = true, RamPending = false };
		Assert.Equal(17408, ZomboidRam.HeapForPlay(moved, 32L * Gib, 20L * Gib));
		moved.RamPending = true;
		Assert.Equal(17408, ZomboidRam.HeapForPlay(moved, 32L * Gib, 20L * Gib));
	}

	[Fact]
	public void ZomboidUntouchedDefaultIs8gAbove16GbAnd6gOtherwise()
	{
		ZomboidState untouched = new ZomboidState();
		Assert.Equal(8192, ZomboidRam.HeapForPlay(untouched, 32L * Gib - 300L * 1024 * 1024, 20L * Gib));
		Assert.Equal(6144, ZomboidRam.HeapForPlay(untouched, 16L * Gib, 10L * Gib));
		Assert.Equal(6144, ZomboidRam.HeapForPlay(untouched, 16L * Gib - 300L * 1024 * 1024, 10L * Gib));
		Assert.Equal(6144, ZomboidRam.HeapForPlay(untouched, 0L, 0L));
	}

	[Fact]
	public void ZomboidDefaultAndSliderStayUnderTheSafeCap()
	{
		RamBudget eight = ZomboidRam.Budget(new ZomboidState(), 8L * Gib, 4L * Gib);
		Assert.Equal(eight.SafeMaxMb, eight.ChosenMb);
		Assert.True(eight.ChosenMb < 6144);
		ZomboidState huge = new ZomboidState { RamMb = 30000, RamSpecified = true };
		RamBudget capped = ZomboidRam.Budget(huge, 16L * Gib, 10L * Gib);
		Assert.Equal(capped.SafeMaxMb, capped.ChosenMb);
	}

	[Fact]
	public void ZomboidPlayWithoutAPendingChangeStillPassesHeapButLeavesTheUserFile()
	{
		string user = NewDir();
		ZomboidLaunchPlan plan = ZomboidLaunch.Prepare(FoundSteam(), user, NewDir(), "", "{\"connect\":{\"host\":\"play.example.com\",\"port\":16261}}", "", 8192, saveMemory: false);
		Assert.True(plan.CanStart);
		Assert.Contains("-Xmx8192m", plan.Url, StringComparison.Ordinal);
		Assert.False(plan.Applied);
		Assert.False(File.Exists(Path.Combine(user, "Zomboid", ZomboidMemory.FileName)));
	}

	[Fact]
	public void MinecraftFillsTheDefaultOnlyWhenNothingWasEverSet()
	{
		string dir = NewDir();
		string instance = Path.Combine(dir, "instance.cfg");
		string global = Path.Combine(dir, "prismlauncher.cfg");
		MinecraftState untouched = new MinecraftState();
		Assert.True(MinecraftGame.ShouldFillDefault(untouched, instance, global));
		File.WriteAllText(instance, "[General]\nOverrideMemory=false\nMaxMemAlloc=4096\n");
		Assert.True(MinecraftGame.ShouldFillDefault(untouched, instance, global));
		File.WriteAllText(global, "[General]\nMaxMemAlloc=3072\n");
		Assert.False(MinecraftGame.ShouldFillDefault(untouched, instance, global));
		Assert.Equal(3072, MinecraftRam.ReadPlayerSetMb(instance, global));
		File.WriteAllText(instance, "[General]\nOverrideMemory=true\nMaxMemAlloc=10240\n");
		Assert.Equal(10240, MinecraftRam.ReadPlayerSetMb(instance, global));
		Assert.False(MinecraftGame.ShouldFillDefault(new MinecraftState { RamMb = 4096, RamSpecified = true }, instance + ".none", global + ".none"));
	}

	[Fact]
	public void MinecraftFillInWritesTheRecommendedHeapAndIsNotRepeated()
	{
		string root = NewDir();
		string game = Path.Combine(root, ".norcom");
		string data = Path.Combine(root, "prism");
		Directory.CreateDirectory(game);
		string prism = Path.Combine(root, "prismlauncher.exe");
		File.WriteAllText(prism, "prism");
		int recommended = RamBudget.Create(16L * Gib, 10L * Gib, 0).ChosenMb;
		Assert.Equal(6144, recommended);
		string cfg = Path.Combine(data, "instances", "NORCOM", "instance.cfg");
		MinecraftState untouched = new MinecraftState();
		Assert.True(MinecraftGame.ShouldFillDefault(untouched, cfg, Path.Combine(data, "prismlauncher.cfg")));
		PrismPreparation applied = PrismPlay.Prepare(prism, Array.Empty<string>(), data, game, "java", recommended, applyMemory: true);
		Assert.True(applied.Applied);
		Assert.True(MinecraftRam.ReadPlayerSetMb(cfg, Path.Combine(data, "prismlauncher.cfg")) > 0);
		Assert.False(MinecraftGame.ShouldFillDefault(untouched, cfg, Path.Combine(data, "prismlauncher.cfg")));
	}

	private static SteamSources FoundSteam()
	{
		string steam = NewDir();
		string game = Path.Combine(steam, "steamapps", "common", "ProjectZomboid");
		Directory.CreateDirectory(game);
		string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[vdf] = "\"path\" \"" + steam.Replace("\\", "\\\\") + "\"\n"
		};
		var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game };
		return new SteamSources
		{
			RegistryPaths = new[] { steam },
			FileExists = path => files.ContainsKey(path),
			DirectoryExists = path => dirs.Contains(path),
			ReadText = path => files.TryGetValue(path, out string text) ? text : null
		};
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-ram-every-play-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
