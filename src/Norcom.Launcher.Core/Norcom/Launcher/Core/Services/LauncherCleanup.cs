using System;
using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class LauncherCleanup
{
	public static IReadOnlyList<string> RemoveObsolete(string liveExe, string norcomHome, string? gameDir, string? tempDir)
	{
		List<string> removed = new List<string>();
		string liveFull = Path.GetFullPath(liveExe);
		string? liveDir = Path.GetDirectoryName(liveFull);
		string recovery = Path.Combine(norcomHome, "recovery");
		if (Directory.Exists(recovery) && !IsOffLimits(recovery, liveFull, liveDir, gameDir))
		{
			foreach (string dir in Directory.EnumerateDirectories(recovery))
			{
				string name = Path.GetFileName(dir);
				if (!name.StartsWith("self-update-", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("install-", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (TryDeleteDirectory(dir, liveFull, liveDir, gameDir))
				{
					removed.Add(dir);
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(liveDir))
		{
			foreach (string relative in LegacyRetirement.Files)
			{
				string path = Path.Combine(liveDir, relative.Replace('/', Path.DirectorySeparatorChar));
				if (TryDeleteFile(path, liveFull, liveDir, gameDir))
				{
					removed.Add(path);
				}
			}
			string graphics = Path.Combine(liveDir, LegacyRetirement.GraphicsTree.Replace('/', Path.DirectorySeparatorChar));
			if (TryDeleteDirectory(graphics, liveFull, liveDir, gameDir))
			{
				removed.Add(graphics);
			}
			string partial = liveFull + ".partial";
			if (TryDeleteFile(partial, liveFull, liveDir, gameDir))
			{
				removed.Add(partial);
			}
		}
		if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
		{
			foreach (string dir in Directory.EnumerateDirectories(tempDir, "norcom-launcher-upd-*"))
			{
				if (TryDeleteDirectory(dir, liveFull, liveDir, gameDir))
				{
					removed.Add(dir);
				}
			}
			foreach (string file in Directory.EnumerateFiles(tempDir, "norcom-apply-*.cmd"))
			{
				if (TryDeleteFile(file, liveFull, liveDir, gameDir))
				{
					removed.Add(file);
				}
			}
		}
		if (removed.Count > 0)
		{
			LauncherLog.Write("launcher cleanup removed " + removed.Count);
		}
		return removed;
	}

	public static string? UpdatePayloadDirectory(string? newExe)
	{
		if (string.IsNullOrWhiteSpace(newExe))
		{
			return null;
		}
		string? dir = Path.GetDirectoryName(Path.GetFullPath(newExe));
		for (int i = 0; dir != null && i < 4; i++)
		{
			if (Path.GetFileName(dir).StartsWith("norcom-launcher-upd-", StringComparison.OrdinalIgnoreCase))
			{
				return dir;
			}
			dir = Path.GetDirectoryName(dir);
		}
		return null;
	}

	private static bool TryDeleteDirectory(string path, string liveExe, string? liveDir, string? gameDir)
	{
		try
		{
			if (!Directory.Exists(path) || IsOffLimits(path, liveExe, liveDir, gameDir))
			{
				return false;
			}
			Directory.Delete(path, recursive: true);
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup skip " + ex.GetType().Name);
			return false;
		}
	}

	private static bool TryDeleteFile(string path, string liveExe, string? liveDir, string? gameDir)
	{
		try
		{
			if (!File.Exists(path) || IsOffLimits(path, liveExe, liveDir, gameDir))
			{
				return false;
			}
			File.Delete(path);
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup skip " + ex.GetType().Name);
			return false;
		}
	}

	public static IReadOnlyList<string> BeforePlay()
	{
		string live = LauncherInstall.CurrentExe();
		return RemoveObsolete(live, NorcomPaths.NorcomHome, NorcomPaths.GameDir, Path.GetTempPath());
	}

	public static bool IsObsoleteCopy(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string full = Path.GetFullPath(path);
		if (Path.GetFileName(full).Equals("NorcomLauncher.previous.exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string name = "";
		string? dir = Path.GetDirectoryName(full);
		for (int i = 0; dir != null && i < 6; i++)
		{
			name = Path.GetFileName(dir);
			if (name.StartsWith("self-update-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("install-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("norcom-launcher-upd-", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			dir = Path.GetDirectoryName(dir);
		}
		return false;
	}

	public static bool IsBlocked(string path)
	{
		string full = Path.GetFullPath(path);
		string file = Path.GetFileName(full);
		if (file.Equals("java.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string[] blocked = new string[8] { ".norcom", ".minecraft", "NorcomAdmin", "Admin", "CrashReceiver", "NORCOM-WPF-Test", "saves", "mods" };
		foreach (string segment in blocked)
		{
			if (ContainsSegment(full, segment))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsOffLimits(string path, string liveExe, string? liveDir, string? gameDir)
	{
		string full = Path.GetFullPath(path);
		if (IsBlocked(full))
		{
			return true;
		}
		if (full.Equals(liveExe, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(liveDir) && full.Equals(Path.GetFullPath(liveDir), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (ContainsSegment(full, "NORCOM-WPF-Test"))
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(gameDir) && IsUnder(full, gameDir))
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(liveDir) && IsUnder(liveExe, full))
		{
			return true;
		}
		return false;
	}

	private static bool ContainsSegment(string path, string segment)
	{
		string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		foreach (string part in parts)
		{
			if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsUnder(string path, string root)
	{
		string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
	}
}
