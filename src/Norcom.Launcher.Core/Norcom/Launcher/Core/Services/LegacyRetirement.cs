using System;
using System.Collections.Generic;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public static class LegacyRetirement
{
	public static readonly string[] Files = new string[14]
	{
		"NORCOM-Launcher.ps1", "Launcher.ps1", "NORCOM-Launcher.vbs", "NORCOM Launcher.exe", "NORCOM-Installer.bat", "Play-NORCOM.bat", "Install NORCOM.cmd", "tools/NorcomExtras.ps1", "tools/NorcomCharacter.ps1", "tools/NorcomUi.cs",
		"tools/NorcomUi.dll", "tools/Check-MinecraftLauncher.ps1", "tools/Test-FabricDependencies.ps1", "tools/packwiz-installer-bootstrap.jar"
	};

	public const string GraphicsTree = "tools/norcom-graphics";

	public static bool BlocksInstall(string processName, string? commandLine)
	{
		string text = FileName(processName);
		if (text.Equals("NORCOM Launcher", StringComparison.OrdinalIgnoreCase) || text.Equals("NORCOM Launcher.exe", StringComparison.OrdinalIgnoreCase) || text.Equals("NorcomLauncher", StringComparison.OrdinalIgnoreCase) || text.Equals("NorcomLauncher.exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!text.Equals("powershell", StringComparison.OrdinalIgnoreCase) && !text.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) && !text.Equals("pwsh", StringComparison.OrdinalIgnoreCase) && !text.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(commandLine))
		{
			return false;
		}
		if (!commandLine.Contains("NORCOM-Launcher.ps1", StringComparison.OrdinalIgnoreCase) && !commandLine.Contains("NORCOM\\Launcher\\Launcher.ps1", StringComparison.OrdinalIgnoreCase))
		{
			return commandLine.Contains("NORCOM/Launcher/Launcher.ps1", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static bool IsLegacyShortcutTarget(string? target)
	{
		if (string.IsNullOrWhiteSpace(target))
		{
			return false;
		}
		string text = FileName(target);
		if (!text.Equals("NORCOM-Launcher.vbs", StringComparison.OrdinalIgnoreCase) && !text.Equals("NORCOM-Launcher.ps1", StringComparison.OrdinalIgnoreCase) && !text.Equals("Launcher.ps1", StringComparison.OrdinalIgnoreCase) && !text.Equals("Play-NORCOM.bat", StringComparison.OrdinalIgnoreCase) && !text.Equals("NORCOM-Installer.bat", StringComparison.OrdinalIgnoreCase) && !text.Equals("Install NORCOM.cmd", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("NORCOM Launcher.exe", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public static string BackupAndRetire(string launcherDir, string? settingsPath, string? shortcutPath, string recoveryRoot)
	{
		string text = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
		string text2 = Path.Combine(recoveryRoot, "install-" + text);
		Directory.CreateDirectory(text2);
		string text3 = Path.Combine(text2, "files");
		Directory.CreateDirectory(text3);
		if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
		{
			File.Copy(settingsPath, Path.Combine(text2, "settings.json"), overwrite: true);
		}
		if (!string.IsNullOrWhiteSpace(shortcutPath) && File.Exists(shortcutPath))
		{
			File.Copy(shortcutPath, Path.Combine(text2, "NORCOM Launcher.lnk"), overwrite: true);
		}
		List<string> list = new List<string>();
		if (Directory.Exists(launcherDir))
		{
			string[] files = Files;
			foreach (string relative in files)
			{
				MoveIfPresent(launcherDir, text3, relative, list);
			}
			string text4 = Path.Combine(launcherDir, "tools/norcom-graphics".Replace('/', Path.DirectorySeparatorChar));
			if (Directory.Exists(text4))
			{
				string text5 = Path.Combine(text3, "tools/norcom-graphics".Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(text5));
				Directory.Move(text4, text5);
				list.Add("tools/norcom-graphics");
			}
		}
		File.WriteAllText(Path.Combine(text2, "RESTORE.txt"), "NORCOM install recovery " + text + Environment.NewLine + "This folder is a copy of the launcher files that were retired, plus settings.json and the desktop shortcut if they existed." + Environment.NewLine + "The game folder (.norcom), worlds, screenshots, options.txt, and servers.dat were not moved." + Environment.NewLine + "To go back to the PowerShell launcher, close NORCOM, then copy the files in the files folder back into the Launcher folder using the same relative names, and copy NORCOM Launcher.lnk back to the desktop. Copy settings.json back beside the Launcher folder." + Environment.NewLine + "Retired:" + Environment.NewLine + string.Join(Environment.NewLine, list) + Environment.NewLine);
		return text2;
	}

	public static void WriteExe(Stream source, string destination)
	{
		string directoryName = Path.GetDirectoryName(destination);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = destination + ".partial";
		using (FileStream destination2 = File.Create(text))
		{
			source.CopyTo(destination2);
		}
		File.Move(text, destination, overwrite: true);
	}

	private static string FileName(string path)
	{
		string text = path.Trim().Trim('"');
		int num = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
		if (num < 0)
		{
			return text;
		}
		string text2 = text;
		int num2 = num + 1;
		return text2.Substring(num2, text2.Length - num2);
	}

	private static void MoveIfPresent(string launcherDir, string keptRoot, string relative, List<string> retired)
	{
		string text = Path.Combine(launcherDir, relative.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(text))
		{
			string text2 = Path.Combine(keptRoot, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			File.Move(text, text2, overwrite: true);
			retired.Add(relative);
		}
	}
}
