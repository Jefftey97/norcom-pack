using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class PackUpgrade
{
	public const string VersionFileName = "launcher-version.txt";

	public const string PendingManifestName = "upgrade-managed.json";

	// State this sweep is allowed to remove. Game saves, options, packs, and configs are not in this list.
	public static readonly string[] DeletableState =
	{
		"cache",
		"managed-files.json",
		"Launcher/applied-pin.json",
		"norcom-applied-pin.txt"
	};

	public static IReadOnlyList<string> OnStartup(string home, string gameDir, string launcherDir, string version, IReadOnlyCollection<string>? keepJarNames = null, IReadOnlyCollection<string>? keepRelativePaths = null)
	{
		Directory.CreateDirectory(launcherDir);
		string stampPath = Path.Combine(launcherDir, VersionFileName);
		string previous = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : "";
		string current = NorcomVersion.Normalize(version);
		bool unversioned = previous.Length == 0;
		if (unversioned && !HasLeftover(home, gameDir, launcherDir))
		{
			File.WriteAllText(stampPath, current + Environment.NewLine);
			return Array.Empty<string>();
		}
		if (!unversioned && string.Equals(NorcomVersion.Normalize(previous), current, StringComparison.Ordinal))
		{
			return Array.Empty<string>();
		}
		List<string> deleted = Clean(home, gameDir, launcherDir, keepJarNames, keepRelativePaths);
		File.WriteAllText(stampPath, current + Environment.NewLine);
		LauncherLog.Write("upgrade sweep " + (unversioned ? "unversioned" : previous) + " -> " + current + " removed=" + deleted.Count);
		return deleted;
	}

	public static bool HasLeftover(string home, string gameDir, string launcherDir)
	{
		if (File.Exists(Path.Combine(home, "managed-files.json")))
		{
			return true;
		}
		if (File.Exists(Path.Combine(launcherDir, PinGate.PrimaryAppliedPinFile)))
		{
			return true;
		}
		if (File.Exists(Path.Combine(gameDir, PinGate.AppliedPinFile)))
		{
			return true;
		}
		string cache = Path.Combine(home, "cache");
		return Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any();
	}

	public static List<string> Clean(string home, string gameDir, string launcherDir, IReadOnlyCollection<string>? keepJarNames, IReadOnlyCollection<string>? keepRelativePaths)
	{
		List<string> deleted = new List<string>();
		string manifestPath = Path.Combine(home, "managed-files.json");
		List<ManagedEntry> previous = ReadManifest(manifestPath);
		string pending = Path.Combine(launcherDir, PendingManifestName);
		if (previous.Count > 0)
		{
			Directory.CreateDirectory(launcherDir);
			File.WriteAllText(pending, JsonSerializer.Serialize(previous));
		}
		deleted.AddRange(DeleteTree(Path.Combine(home, "cache")));
		deleted.AddRange(DeleteFile(manifestPath));
		deleted.AddRange(DeleteFile(Path.Combine(launcherDir, PinGate.PrimaryAppliedPinFile)));
		deleted.AddRange(DeleteFile(Path.Combine(gameDir, PinGate.AppliedPinFile)));
		if (keepJarNames != null || keepRelativePaths != null)
		{
			deleted.AddRange(RetireEntries(gameDir, previous, keepRelativePaths, keepJarNames));
			DeleteFile(pending);
		}
		return deleted;
	}

	public static IReadOnlyList<string> RetireSaved(string launcherDir, string gameDir, IReadOnlyCollection<string>? keepRelative, IReadOnlyCollection<string>? keepJarNames)
	{
		string pending = Path.Combine(launcherDir, PendingManifestName);
		if (!File.Exists(pending))
		{
			return Array.Empty<string>();
		}
		List<ManagedEntry> previous = ReadManifest(pending);
		List<string> deleted = RetireEntries(gameDir, previous, keepRelative, keepJarNames);
		DeleteFile(pending);
		if (deleted.Count > 0)
		{
			LauncherLog.Write("upgrade retire " + deleted.Count);
		}
		return deleted;
	}

	public static bool CanRetire(string? relative)
	{
		string text = (relative ?? "").Replace('\\', '/');
		if (text.Length == 0 || text.Contains("..", StringComparison.Ordinal) || IsProtected(text))
		{
			return false;
		}
		return text.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && text.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
	}

	public static bool IsProtected(string? relative)
	{
		string text = (relative ?? "").Replace('\\', '/').TrimStart('/');
		string[] blocked = new string[10] { "options.txt", "optionsof.txt", "servers.dat", "saves/", "screenshots/", "shaderpacks/", "resourcepacks/", "config/", "norcom-skins/", "logs/" };
		foreach (string entry in blocked)
		{
			string name = entry.TrimEnd('/');
			if (text.Equals(name, StringComparison.OrdinalIgnoreCase) || text.StartsWith(entry, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static List<string> RetireEntries(string gameDir, IReadOnlyList<ManagedEntry> previous, IReadOnlyCollection<string>? keepRelative, IReadOnlyCollection<string>? keepJarNames)
	{
		HashSet<string> paths = new HashSet<string>(keepRelative ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		HashSet<string> names = new HashSet<string>(keepJarNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		List<string> deleted = new List<string>();
		foreach (ManagedEntry entry in previous)
		{
			if (entry.Preserve)
			{
				continue;
			}
			string rel = (entry.RelativePath ?? "").Replace('\\', '/');
			if (!CanRetire(rel))
			{
				continue;
			}
			string file = Path.GetFileName(rel);
			if (paths.Contains(rel) || names.Contains(file))
			{
				continue;
			}
			deleted.AddRange(DeleteFile(Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar))));
		}
		return deleted;
	}

	private static List<ManagedEntry> ReadManifest(string path)
	{
		if (!File.Exists(path))
		{
			return new List<ManagedEntry>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<ManagedEntry>>(File.ReadAllText(path)) ?? new List<ManagedEntry>();
		}
		catch (JsonException)
		{
			return new List<ManagedEntry>();
		}
	}

	private static List<string> DeleteTree(string root)
	{
		List<string> deleted = new List<string>();
		if (!Directory.Exists(root) || IsReparse(root))
		{
			return deleted;
		}
		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
		{
			if (IsReparse(file))
			{
				continue;
			}
			deleted.AddRange(DeleteFile(file));
		}
		foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
		{
			if (IsReparse(dir))
			{
				continue;
			}
			try
			{
				if (!Directory.EnumerateFileSystemEntries(dir).Any())
				{
					Directory.Delete(dir);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				LauncherLog.Write("upgrade sweep " + ex.GetType().Name);
			}
		}
		try
		{
			if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
			{
				Directory.Delete(root);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("upgrade sweep " + ex.GetType().Name);
		}
		return deleted;
	}

	private static List<string> DeleteFile(string path)
	{
		List<string> deleted = new List<string>();
		try
		{
			if (!File.Exists(path) || IsReparse(path))
			{
				return deleted;
			}
			File.Delete(path);
			deleted.Add(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("upgrade sweep " + ex.GetType().Name);
		}
		return deleted;
	}

	private static bool IsReparse(string path)
	{
		try
		{
			return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return true;
		}
	}
}
