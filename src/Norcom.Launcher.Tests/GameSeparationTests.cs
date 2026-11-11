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

	[Fact]
	public void OnlyGameHostReferencesBothGameModules()
	{
		string[] minecraft = { "Games.Minecraft", "MinecraftGame", "MinecraftGraphics", "MinecraftState", "MinecraftSettings", "MinecraftRunGate" };
		string[] zomboid = { "Games.Zomboid", "ZomboidGame", "ZomboidGraphics", "ZomboidState", "ZomboidSettings", "ZomboidSession" };
		foreach (string file in SourceFiles())
		{
			if (string.Equals(Path.GetFileName(file), "GameHost.cs", StringComparison.Ordinal))
			{
				continue;
			}
			string text = File.ReadAllText(file);
			bool mentionsMinecraft = ContainsAny(text, minecraft);
			bool mentionsZomboid = ContainsAny(text, zomboid);
			Assert.False(mentionsMinecraft && mentionsZomboid, file);
		}
	}

	private static bool ContainsAny(string text, string[] tokens)
	{
		for (int i = 0; i < tokens.Length; i++)
		{
			if (text.Contains(tokens[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<string> SourceFiles()
	{
		string root = RepoRoot();
		string[] roots =
		{
			Path.Combine(root, "src", "Norcom.Launcher.Core"),
			Path.Combine(root, "src", "Norcom.Launcher", "Norcom.Launcher.ViewModels"),
			Path.Combine(root, "src", "Norcom.Launcher", "views")
		};
		for (int i = 0; i < roots.Length; i++)
		{
			foreach (string file in Directory.EnumerateFiles(roots[i], "*.*", SearchOption.AllDirectories))
			{
				if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string slash = file.Replace('\\', '/');
				if (slash.Contains("/Games/Minecraft/", StringComparison.Ordinal) || slash.Contains("/Games/Zomboid/", StringComparison.Ordinal))
				{
					continue;
				}
				yield return file;
			}
		}
	}

	private static string RepoRoot()
	{
		string dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			if (Directory.Exists(Path.Combine(dir, "src", "Norcom.Launcher.Core")))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir);
		}
		throw new DirectoryNotFoundException("norcom-pack");
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
