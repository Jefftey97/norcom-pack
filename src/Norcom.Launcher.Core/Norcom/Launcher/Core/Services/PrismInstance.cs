using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Norcom.Launcher.Core.Services;

public static class PrismInstance
{
	public const string MarkerName = "norcom-managed.txt";

	public static void Ensure(string dataDir, string gameDir, string javaPath, int ramMb)
	{
		string text = Path.Combine(dataDir, "instances", "NORCOM");
		if (Overlaps(text, gameDir))
		{
			throw new InvalidOperationException("The NORCOM game folder and the Prism instance overlap. Nothing was linked.");
		}
		if (Directory.Exists(text) && !IsOurs(text))
		{
			throw new InvalidOperationException("A Prism instance named NORCOM already exists and was not created by this launcher. It was left unchanged, and Minecraft was not started.");
		}
		Directory.CreateDirectory(text);
		File.WriteAllText(Path.Combine(text, "norcom-managed.txt"), "NORCOM");
		WriteConfig(Path.Combine(text, "instance.cfg"), javaPath, ramMb);
		WritePack(Path.Combine(text, "mmc-pack.json"));
		EnsureGameLink(Path.Combine(text, "minecraft"), gameDir);
	}

	public static void WriteConfig(string path, string javaPath, int ramMb)
	{
		string value = javaPath.Replace('\\', '/');
		int num = Math.Clamp(ramMb / 4, 512, ramMb);
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ConfigVersion"] = "1.3",
			["InstanceType"] = "OneSix",
			["name"] = "NORCOM",
			["OverrideJavaLocation"] = "true",
			["JavaPath"] = value,
			["AutomaticJava"] = "false",
			["OverrideMemory"] = "true",
			["MinMemAlloc"] = num.ToString(),
			["MaxMemAlloc"] = Math.Clamp(ramMb, 2048, 16384).ToString()
		};
		List<string> list = new List<string>();
		if (File.Exists(path))
		{
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				string text2 = text.Trim();
				if (text2.Length == 0 || text2.StartsWith('#') || text2.StartsWith('['))
				{
					list.Add(text);
					continue;
				}
				int num2 = text2.IndexOf('=');
				if (num2 <= 0)
				{
					list.Add(text);
					continue;
				}
				string text3 = text2.Substring(0, num2).Trim();
				bool flag = dictionary.ContainsKey(text3);
				if (!flag)
				{
					bool flag2 = ((text3 == "UseAccountForInstance" || text3 == "InstanceAccountId") ? true : false);
					flag = flag2;
				}
				if (!flag)
				{
					list.Add(text);
				}
			}
		}
		else
		{
			list.Add("[General]");
		}
		foreach (KeyValuePair<string, string> item in dictionary)
		{
			list.Add(item.Key + "=" + item.Value);
		}
		File.WriteAllLines(path, list);
	}

	public static void WritePack(string path)
	{
		if (!File.Exists(path) || !PackMatches(path))
		{
			var value = new
			{
				formatVersion = 1,
				components = new object[2]
				{
					new
					{
						uid = "net.minecraft",
						version = "26.3",
						important = true
					},
					new
					{
						uid = "net.fabricmc.fabric-loader",
						version = "0.20.1"
					}
				}
			};
			File.WriteAllText(path, JsonSerializer.Serialize(value));
		}
	}

	public static bool EndsWithNorcomGame(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string trimmed = path.Trim().TrimEnd('\\', '/');
		if (trimmed.EndsWith("\\.norcom", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith("/.norcom", StringComparison.OrdinalIgnoreCase) || trimmed.Equals(".norcom", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		try
		{
			string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return string.Equals(Path.GetFileName(full), ".norcom", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	public static void EnsureGameLink(string linkPath, string gameDir)
	{
		string fullPath = Path.GetFullPath(gameDir);
		if (!EndsWithNorcomGame(fullPath))
		{
			throw new InvalidOperationException("Play refused. The game folder must end with \\.norcom.");
		}
		Directory.CreateDirectory(gameDir);
		if (IsLink(linkPath))
		{
			FileSystemInfo fileSystemInfo = Directory.ResolveLinkTarget(linkPath, returnFinalTarget: true);
			if (fileSystemInfo != null && PathsEqual(fileSystemInfo.FullName, fullPath))
			{
				return;
			}
			Directory.Delete(linkPath);
		}
		else if (Directory.Exists(linkPath) || File.Exists(linkPath))
		{
			throw new InvalidOperationException("NORCOM's Prism instance already has a minecraft folder that is not the NORCOM game folder. It was not replaced, and Minecraft was not started.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
		if (OperatingSystem.IsWindows())
		{
			CreateJunction(linkPath, fullPath);
		}
		else
		{
			Directory.CreateSymbolicLink(linkPath, fullPath);
		}
	}

	private static bool IsOurs(string instanceDir)
	{
		string path = Path.Combine(instanceDir, "norcom-managed.txt");
		if (File.Exists(path))
		{
			return File.ReadAllText(path).Trim() == "NORCOM";
		}
		return false;
	}

	private static bool PackMatches(string path)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			if (!jsonDocument.RootElement.TryGetProperty("components", out var value))
			{
				return false;
			}
			string text = "";
			string text2 = "";
			foreach (JsonElement item in value.EnumerateArray())
			{
				JsonElement value2;
				string obj = (item.TryGetProperty("uid", out value2) ? value2.GetString() : "");
				JsonElement value3;
				string text3 = (item.TryGetProperty("version", out value3) ? value3.GetString() : "");
				if (obj == "net.minecraft")
				{
					text = text3 ?? "";
				}
				if (obj == "net.fabricmc.fabric-loader")
				{
					text2 = text3 ?? "";
				}
			}
			return text == "26.3" && text2 == "0.20.1";
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLink(string path)
	{
		if (Directory.Exists(path))
		{
			return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
		}
		return false;
	}

	private static bool Overlaps(string instanceDir, string gameDir)
	{
		ReadOnlySpan<char> readOnlySpan = Path.GetFullPath(instanceDir).TrimEnd(Path.DirectorySeparatorChar);
		char reference = Path.DirectorySeparatorChar;
		string text = string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference));
		ReadOnlySpan<char> readOnlySpan2 = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
		reference = Path.DirectorySeparatorChar;
		string text2 = string.Concat(readOnlySpan2, new ReadOnlySpan<char>(ref reference));
		if (!text.StartsWith(text2, StringComparison.OrdinalIgnoreCase))
		{
			return text2.StartsWith(text, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool PathsEqual(string left, string right)
	{
		return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private static void CreateJunction(string linkPath, string target)
	{
		using Process process = Process.Start(new ProcessStartInfo("cmd.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			ArgumentList = { "/c", "mklink", "/J", linkPath, target }
		}) ?? throw new InvalidOperationException("NORCOM's Prism instance already has a minecraft folder that is not the NORCOM game folder. It was not replaced, and Minecraft was not started.");
		process.WaitForExit();
		if (process.ExitCode != 0 || !Directory.Exists(linkPath))
		{
			throw new InvalidOperationException("NORCOM's Prism instance already has a minecraft folder that is not the NORCOM game folder. It was not replaced, and Minecraft was not started.");
		}
	}
}
