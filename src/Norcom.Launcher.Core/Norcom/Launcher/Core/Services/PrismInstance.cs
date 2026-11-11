using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class PrismInstance
{
	public const string MarkerName = "norcom-managed.txt";

	public static bool HasNorcomInstance(string? dataDir)
	{
		if (string.IsNullOrWhiteSpace(dataDir))
		{
			return false;
		}
		string instance = Path.Combine(dataDir, "instances", "NORCOM");
		return File.Exists(Path.Combine(instance, "instance.cfg"));
	}

	public static bool Ensure(string dataDir, string gameDir, string javaPath, int ramMb, bool applyMemory = false)
	{
		string text = Path.Combine(dataDir, "instances", "NORCOM");
		if (Overlaps(text, gameDir))
		{
			throw new InvalidOperationException("The NORCOM game folder and the Prism instance overlap. Nothing was linked.");
		}
		bool foreign = Directory.Exists(text) && !IsOurs(text);
		Directory.CreateDirectory(text);
		if (foreign)
		{
			LauncherLog.Write("prism " + PrismMessages.Claimed);
		}
		File.WriteAllText(Path.Combine(text, "norcom-managed.txt"), "NORCOM");
		WriteConfig(Path.Combine(text, "instance.cfg"), javaPath, ramMb, applyMemory);
		WritePack(Path.Combine(text, "mmc-pack.json"));
		EnsureGameLink(Path.Combine(text, "minecraft"), gameDir);
		return applyMemory;
	}

	public static void WriteConfig(string path, string javaPath, int ramMb, bool applyMemory = false)
	{
		string value = javaPath.Replace('\\', '/');
		int heap = Math.Max(512, ramMb);
		string heapText = heap.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["ConfigVersion"] = "1.3",
			["InstanceType"] = "OneSix",
			["name"] = "NORCOM",
			["OverrideJavaLocation"] = "true",
			["JavaPath"] = value,
			["AutomaticJava"] = "false"
		};
		if (applyMemory)
		{
			dictionary["OverrideMemory"] = "true";
			dictionary["MinMemAlloc"] = heapText;
			dictionary["MaxMemAlloc"] = heapText;
		}
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
		else if (File.Exists(linkPath) && !Directory.Exists(linkPath))
		{
			throw new InvalidOperationException(PrismMessages.LinkBlocked);
		}
		else if (Directory.Exists(linkPath))
		{
			try
			{
				PreserveSaves(linkPath, fullPath);
				Directory.Move(linkPath, KeptPath(linkPath));
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(PrismMessages.LinkKept, ex);
			}
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

	public static int PreserveSaves(string minecraftDir, string gameDir)
	{
		if (string.IsNullOrWhiteSpace(minecraftDir) || !Directory.Exists(minecraftDir))
		{
			return 0;
		}
		int copied = CopyMissingDirectory(Path.Combine(minecraftDir, "saves"), Path.Combine(gameDir, "saves"));
		CopyIfMissing(Path.Combine(minecraftDir, "options.txt"), Path.Combine(gameDir, "options.txt"));
		CopyIfMissing(Path.Combine(minecraftDir, "optionsof.txt"), Path.Combine(gameDir, "optionsof.txt"));
		return copied;
	}

	public static string KeptPath(string minecraftDir)
	{
		string kept = minecraftDir + ".norcom-kept";
		if (!Directory.Exists(kept) && !File.Exists(kept))
		{
			return kept;
		}
		return minecraftDir + ".norcom-kept-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
	}

	private static int CopyMissingDirectory(string source, string dest)
	{
		if (!Directory.Exists(source))
		{
			return 0;
		}
		Directory.CreateDirectory(dest);
		int copied = 0;
		foreach (string dir in Directory.EnumerateDirectories(source))
		{
			string target = Path.Combine(dest, Path.GetFileName(dir));
			if (Directory.Exists(target))
			{
				continue;
			}
			CopyTree(dir, target);
			copied++;
		}
		return copied;
	}

	private static void CopyTree(string source, string dest)
	{
		Directory.CreateDirectory(dest);
		foreach (string file in Directory.EnumerateFiles(source))
		{
			string target = Path.Combine(dest, Path.GetFileName(file));
			if (!File.Exists(target))
			{
				File.Copy(file, target, overwrite: false);
			}
		}
		foreach (string dir in Directory.EnumerateDirectories(source))
		{
			CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)));
		}
	}

	private static void CopyIfMissing(string source, string dest)
	{
		if (!File.Exists(source) || File.Exists(dest))
		{
			return;
		}
		string? parent = Path.GetDirectoryName(dest);
		if (!string.IsNullOrEmpty(parent))
		{
			Directory.CreateDirectory(parent);
		}
		File.Copy(source, dest, overwrite: false);
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
