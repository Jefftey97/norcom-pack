using Xunit;

namespace Norcom.Launcher.Tests;

public class GameSeparationTests
{
	[Fact]
	public void MinecraftModuleDoesNotReferenceZomboidTypes()
	{
		AssertNoType(Folder("Minecraft"), "Zomboid");
	}

	[Fact]
	public void ZomboidModuleDoesNotReferenceMinecraftTypes()
	{
		AssertNoType(Folder("Zomboid"), "Minecraft");
	}

	private static void AssertNoType(string folder, string other)
	{
		Assert.True(Directory.Exists(folder), folder);
		foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
		{
			string text = File.ReadAllText(file);
			Assert.DoesNotContain(other, text, StringComparison.Ordinal);
		}
	}

	private static string Folder(string name)
	{
		string dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			string candidate = Path.Combine(dir, "src", "Norcom.Launcher.Core", "Norcom", "Launcher", "Core", "Games", name);
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
			dir = Path.GetDirectoryName(dir);
		}
		throw new DirectoryNotFoundException(name);
	}
}
