using Xunit;

namespace Norcom.Launcher.Tests;

public class ChoiceAndDialogTests
{
	[Fact]
	public void ActivePackAndPresetUseTheSharedSelectedStyle()
	{
		string theme = File.ReadAllText(Repo("src", "Norcom.Launcher", "theme", "Norcom.xaml"));
		Assert.Contains("x:Key=\"SelectedChoiceButton\"", theme, StringComparison.Ordinal);
		Assert.Contains("BasedOn=\"{StaticResource PrimaryButton}\"", theme, StringComparison.Ordinal);
		string menu = File.ReadAllText(Repo("src", "Norcom.Launcher", "views", "GraphicsMenuView.xaml"));
		Assert.Contains("x:Name=\"PatrixPackButton\"", menu, StringComparison.Ordinal);
		Assert.Contains("x:Name=\"AlacrityPackButton\"", menu, StringComparison.Ordinal);
		Assert.Contains("Command=\"{Binding ChooseTexturePackCommand}\"", menu, StringComparison.Ordinal);
		Assert.Contains("CommandParameter=\"Patrix\"", menu, StringComparison.Ordinal);
		Assert.Contains("CommandParameter=\"Alacrity\"", menu, StringComparison.Ordinal);
		string code = File.ReadAllText(Repo("src", "Norcom.Launcher", "views", "GraphicsMenuView.xaml.cs"));
		Assert.Contains("SelectedChoiceButton", code, StringComparison.Ordinal);
		Assert.Contains("TexturePackChoice.Patrix", code, StringComparison.Ordinal);
		Assert.Contains("TexturePackChoice.Alacrity", code, StringComparison.Ordinal);
		Assert.Contains("nameof(ShellViewModel.ActiveTexturePack)", code, StringComparison.Ordinal);
		Assert.Contains("_profile.Preset", code, StringComparison.Ordinal);
		string shell = File.ReadAllText(Repo("src", "Norcom.Launcher", "Norcom.Launcher.ViewModels", "ShellViewModel.cs"));
		Assert.Contains("ChooseTexturePackCommand", shell, StringComparison.Ordinal);
		Assert.Contains("new RelayCommand<string>(ChooseTexturePack)", shell, StringComparison.Ordinal);
		Assert.Contains("InputOnAccentBrush", theme, StringComparison.Ordinal);
	}

	[Fact]
	public void ConfirmDialogsAreOwnedAndEscCancels()
	{
		string prompt = File.ReadAllText(Repo("src", "Norcom.Launcher", "views", "PromptWindow.xaml"));
		Assert.Contains("IsCancel=\"True\"", prompt, StringComparison.Ordinal);
		Assert.Contains("Name=\"NoButton\"", prompt, StringComparison.Ordinal);
		Assert.Contains("Name=\"YesButton\"", prompt, StringComparison.Ordinal);
		string dialogs = File.ReadAllText(Repo("src", "Norcom.Launcher", "views", "LauncherDialogs.cs"));
		Assert.Contains("WindowStartupLocation.CenterOwner", dialogs, StringComparison.Ordinal);
		Assert.Contains("dialog.Owner = owner", dialogs, StringComparison.Ordinal);
		Assert.Contains("owner.Closing", dialogs, StringComparison.Ordinal);
		string shell = File.ReadAllText(Repo("src", "Norcom.Launcher", "views", "ShellWindow.xaml.cs"));
		Assert.Contains("DismissOwned", shell, StringComparison.Ordinal);
		string views = Repo("src", "Norcom.Launcher", "views");
		foreach (string file in Directory.EnumerateFiles(views, "*.cs"))
		{
			string name = Path.GetFileName(file);
			if (name.Equals("App.cs", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string text = File.ReadAllText(file);
			Assert.DoesNotContain("MessageBox.Show", text, StringComparison.Ordinal);
		}
		string viewModels = Repo("src", "Norcom.Launcher", "Norcom.Launcher.ViewModels");
		foreach (string file in Directory.EnumerateFiles(viewModels, "*.cs"))
		{
			Assert.DoesNotContain("MessageBox.Show", File.ReadAllText(file), StringComparison.Ordinal);
		}
	}

	private static string Repo(params string[] parts)
	{
		string dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			string candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
			if (File.Exists(candidate) || Directory.Exists(candidate))
			{
				return candidate;
			}
			dir = Path.GetDirectoryName(dir);
		}
		throw new DirectoryNotFoundException(string.Join("/", parts));
	}
}
