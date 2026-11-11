using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Norcom.Launcher.Tests;

public class InputContrastTests
{
	[Fact]
	public void SharedInputColorsClearWcagAa()
	{
		string theme = File.ReadAllText(RepoFile("src", "Norcom.Launcher", "theme", "Norcom.xaml"));
		Assert.Contains("TargetType=\"ComboBox\"", theme, StringComparison.Ordinal);
		Assert.Contains("TargetType=\"ComboBoxItem\"", theme, StringComparison.Ordinal);
		Assert.Contains("TargetType=\"TextBox\"", theme, StringComparison.Ordinal);
		Assert.Contains("TargetType=\"PasswordBox\"", theme, StringComparison.Ordinal);
		Dictionary<string, string> colors = BrushColors(theme);
		Assert.True(Contrast(colors["TextBrush"], colors["InputBrush"]) >= 4.5);
		Assert.True(Contrast(colors["TextBrush"], colors["InputHoverBrush"]) >= 4.5);
		Assert.True(Contrast(colors["TextBrush"], colors["InputSelectedBrush"]) >= 4.5);
		Assert.True(Contrast(colors["MutedBrush"], colors["InputBrush"]) >= 4.5);
		Assert.True(Contrast(colors["InputOnAccentBrush"], colors["BlueBrush"]) >= 4.5);
		Assert.True(Contrast(colors["TextBrush"], colors["BackgroundBrush"]) >= 4.5);
		Assert.True(Contrast(colors["TextBrush"], colors["SurfaceBrush"]) >= 4.5);
		Assert.True(Contrast(colors["InputBrush"], colors["InputBrush"]) < 4.5);
	}

	[Fact]
	public void LauncherInputsDoNotPaintTheirOwnForeground()
	{
		string views = RepoFile("src", "Norcom.Launcher", "views");
		foreach (string file in Directory.EnumerateFiles(views, "*.*", SearchOption.AllDirectories))
		{
			string name = Path.GetFileName(file);
			if (name.Equals("Norcom.xaml", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string text = File.ReadAllText(file);
			if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
			{
				Assert.DoesNotMatch(new Regex("<(ComboBox|TextBox|PasswordBox)\\b[^>]*\\b(Foreground|Background)\\s*=", RegexOptions.Singleline), text);
			}
			if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
			{
				foreach (Match match in Regex.Matches(text, @"new\s+(ComboBox|TextBox|PasswordBox)\s*\{(?<body>[^}]*)\}"))
				{
					Assert.DoesNotContain("Foreground", match.Groups["body"].Value, StringComparison.Ordinal);
					Assert.DoesNotContain("Background", match.Groups["body"].Value, StringComparison.Ordinal);
				}
			}
		}
	}

	private static Dictionary<string, string> BrushColors(string theme)
	{
		Dictionary<string, string> colors = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (Match match in Regex.Matches(theme, "x:Key=\"(?<key>[^\"]+)\"\\s+Color=\"#(?<hex>[0-9A-Fa-f]{6})\""))
		{
			colors[match.Groups["key"].Value] = match.Groups["hex"].Value;
		}
		return colors;
	}

	private static double Contrast(string hexA, string hexB)
	{
		double lighter = Math.Max(Luminance(hexA), Luminance(hexB));
		double darker = Math.Min(Luminance(hexA), Luminance(hexB));
		return (lighter + 0.05) / (darker + 0.05);
	}

	private static double Luminance(string hex)
	{
		double Channel(int offset)
		{
			double value = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
			return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
		}
		double r = Channel(0);
		double g = Channel(2);
		double b = Channel(4);
		return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
	}

	private static string RepoFile(params string[] parts)
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
		throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
	}
}
