using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Norcom.Launcher.Core.Services;

public static class LauncherInstall
{
	public static string CurrentExe()
	{
		return Resolve(Environment.ProcessPath, FriendInstaller.InstalledExePath);
	}

	public static string Resolve(string? processPath, string canonicalInstall)
	{
		if (IsAltInstall(processPath, canonicalInstall))
		{
			return Path.GetFullPath(canonicalInstall);
		}
		if (IsRunningLauncher(processPath) && !LauncherCleanup.IsObsoleteCopy(processPath))
		{
			return Path.GetFullPath(processPath!);
		}
		return Path.GetFullPath(canonicalInstall);
	}

	public static bool IsRunningLauncher(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		return Path.GetFileName(path).Equals(FriendInstaller.ExeName, StringComparison.OrdinalIgnoreCase);
	}

	public static string AltExeFor(string canonicalInstall)
	{
		if (string.IsNullOrWhiteSpace(canonicalInstall))
		{
			return "";
		}
		string full;
		try
		{
			full = Path.GetFullPath(canonicalInstall);
		}
		catch
		{
			return "";
		}
		string? launcherDir = Path.GetDirectoryName(full);
		string? home = string.IsNullOrEmpty(launcherDir) ? null : Path.GetDirectoryName(launcherDir);
		string? local = string.IsNullOrEmpty(home) ? null : Path.GetDirectoryName(home);
		if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(home) || string.IsNullOrEmpty(launcherDir))
		{
			return "";
		}
		if (!Path.GetFileName(home).Equals("NORCOM", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(launcherDir).Equals("Launcher", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).Equals(FriendInstaller.ExeName, StringComparison.OrdinalIgnoreCase))
		{
			return "";
		}
		return Path.Combine(local, "NorcomLauncher", FriendInstaller.ExeName);
	}

	public static bool IsAltInstall(string? processPath, string canonicalInstall)
	{
		string alt = AltExeFor(canonicalInstall);
		if (string.IsNullOrEmpty(alt) || string.IsNullOrWhiteSpace(processPath))
		{
			return false;
		}
		try
		{
			return string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(alt), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	public static bool IsPrimaryInstall(string? processPath, string canonicalInstall)
	{
		if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(canonicalInstall))
		{
			return false;
		}
		try
		{
			return string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(canonicalInstall), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	public static bool ShouldPromoteAlt(string? processPath, string canonicalInstall)
	{
		return IsAltInstall(processPath, canonicalInstall);
	}

	public static bool ShouldCopyRunningToPrimary(bool runningFromAlt, string? runningVersion, string? primaryVersion, string? manifestVersion)
	{
		if (!runningFromAlt)
		{
			return false;
		}
		if (VersionCompare.Compare(manifestVersion, runningVersion) > 0)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(primaryVersion))
		{
			return true;
		}
		return VersionCompare.Compare(primaryVersion, runningVersion) < 0;
	}

	public static bool SameBits(string left, string right)
	{
		FileInfo a = new FileInfo(left);
		FileInfo b = new FileInfo(right);
		if (!a.Exists || !b.Exists || a.Length != b.Length)
		{
			return false;
		}
		using SHA256 ha = SHA256.Create();
		using SHA256 hb = SHA256.Create();
		using FileStream sa = File.OpenRead(left);
		using FileStream sb = File.OpenRead(right);
		return ha.ComputeHash(sa).AsSpan().SequenceEqual(hb.ComputeHash(sb));
	}

	public static void CopyLauncher(string sourceExe, string destExe)
	{
		if (string.IsNullOrWhiteSpace(sourceExe) || !File.Exists(sourceExe))
		{
			throw new FileNotFoundException("NorcomLauncher.exe was not found.", sourceExe);
		}
		string? destDir = Path.GetDirectoryName(Path.GetFullPath(destExe));
		if (string.IsNullOrEmpty(destDir))
		{
			throw new InvalidOperationException("Launcher destination is missing.");
		}
		Directory.CreateDirectory(destDir);
		if (!string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(destExe), StringComparison.OrdinalIgnoreCase))
		{
			File.Copy(Path.GetFullPath(sourceExe), Path.GetFullPath(destExe), overwrite: true);
		}
		string? sourceDir = Path.GetDirectoryName(Path.GetFullPath(sourceExe));
		string sourcePayload = sourceDir == null ? "" : Path.Combine(sourceDir, PayloadStamp.FileName);
		if (!string.IsNullOrEmpty(sourcePayload) && File.Exists(sourcePayload))
		{
			File.Copy(sourcePayload, Path.Combine(destDir, PayloadStamp.FileName), overwrite: true);
		}
		else
		{
			PayloadStamp.WriteBeside(destExe, NorcomVersion.Current);
		}
	}

	public static bool TrySyncAlt(string primaryExe)
	{
		string alt = AltExeFor(primaryExe);
		if (string.IsNullOrEmpty(alt) || !File.Exists(primaryExe))
		{
			return false;
		}
		string? altDir = Path.GetDirectoryName(alt);
		if (string.IsNullOrEmpty(altDir))
		{
			return false;
		}
		if (!File.Exists(alt) && !Directory.Exists(altDir))
		{
			return false;
		}
		if (File.Exists(alt) && SameBits(primaryExe, alt))
		{
			string primaryPayload = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(primaryExe)) ?? "", PayloadStamp.FileName);
			string altPayload = Path.Combine(altDir, PayloadStamp.FileName);
			if (!File.Exists(primaryPayload) || (File.Exists(altPayload) && SameBits(primaryPayload, altPayload)))
			{
				return false;
			}
		}
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				CopyLauncher(primaryExe, alt);
				return true;
			}
			catch (IOException) when (attempt < 4)
			{
				Thread.Sleep(400);
			}
		}
	}

	public static string BuildPromoteScript(int pid, string stagedExe, string primaryExe, string? altExe)
	{
		StringBuilder script = new StringBuilder();
		script.AppendLine("@echo off");
		script.AppendLine("setlocal");
		script.AppendLine(":wait");
		script.Append("tasklist /FI \"PID eq ").Append(pid).AppendLine("\" | findstr /I \"" + pid + "\" >nul");
		script.AppendLine("if not errorlevel 1 (");
		script.AppendLine("  timeout /t 1 /nobreak >nul");
		script.AppendLine("  goto wait");
		script.AppendLine(")");
		script.AppendLine("set TRY=0");
		script.AppendLine(":swap");
		script.Append("copy /Y \"").Append(stagedExe).Append("\" \"").Append(primaryExe).AppendLine("\" >nul");
		script.AppendLine("if errorlevel 1 (");
		script.AppendLine("  set /a TRY+=1");
		script.AppendLine("  if %TRY% GEQ 20 goto start");
		script.AppendLine("  timeout /t 1 /nobreak >nul");
		script.AppendLine("  goto swap");
		script.AppendLine(")");
		string? stagedDir = Path.GetDirectoryName(stagedExe);
		if (!string.IsNullOrEmpty(stagedDir))
		{
			string payload = Path.Combine(stagedDir, PayloadStamp.FileName);
			string? primaryDir = Path.GetDirectoryName(primaryExe);
			if (File.Exists(payload) && !string.IsNullOrEmpty(primaryDir))
			{
				script.Append("copy /Y \"").Append(payload).Append("\" \"").Append(Path.Combine(primaryDir, PayloadStamp.FileName)).AppendLine("\" >nul");
			}
		}
		if (!string.IsNullOrWhiteSpace(altExe) && !string.Equals(Path.GetFullPath(altExe), Path.GetFullPath(primaryExe), StringComparison.OrdinalIgnoreCase))
		{
			script.Append("copy /Y \"").Append(stagedExe).Append("\" \"").Append(altExe).AppendLine("\" >nul");
		}
		script.AppendLine(":start");
		script.Append("start \"\" \"").Append(primaryExe).AppendLine("\"");
		if (!string.IsNullOrEmpty(stagedDir))
		{
			script.Append("rmdir /s /q \"").Append(stagedDir).AppendLine("\" >nul 2>&1");
		}
		script.AppendLine("exit /b 0");
		return script.ToString();
	}

	public static void PromoteAlt(string runningExe, int pid)
	{
		string primary = Path.GetFullPath(FriendInstaller.InstalledExePath);
		string? primaryVersion = NorcomVersion.TryReadExeVersion(primary);
		string? runningVersion = NorcomVersion.TryReadExeVersion(runningExe) ?? NorcomVersion.Current;
		bool keepPrimary = File.Exists(primary) && VersionCompare.Compare(primaryVersion, runningVersion) >= 0;
		string stagedDir = Path.Combine(Path.GetTempPath(), "norcom-promote-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagedDir);
		string stagedExe = Path.Combine(stagedDir, FriendInstaller.ExeName);
		File.Copy(keepPrimary ? primary : runningExe, stagedExe, overwrite: true);
		string sourcePayloadDir = Path.GetDirectoryName(keepPrimary ? primary : Path.GetFullPath(runningExe)) ?? "";
		string sourcePayload = Path.Combine(sourcePayloadDir, PayloadStamp.FileName);
		if (File.Exists(sourcePayload))
		{
			File.Copy(sourcePayload, Path.Combine(stagedDir, PayloadStamp.FileName), overwrite: true);
		}
		else
		{
			PayloadStamp.WriteBeside(stagedExe, keepPrimary ? (primaryVersion ?? NorcomVersion.Current) : runningVersion);
		}
		string alt = AltExeFor(primary);
		string? altTarget = null;
		if (!string.IsNullOrEmpty(alt))
		{
			string? altDir = Path.GetDirectoryName(alt);
			if (!string.IsNullOrEmpty(altDir) && (File.Exists(alt) || Directory.Exists(altDir)))
			{
				altTarget = alt;
			}
		}
		string scriptPath = Path.Combine(Path.GetTempPath(), "norcom-promote-" + Guid.NewGuid().ToString("N") + ".cmd");
		File.WriteAllText(scriptPath, BuildPromoteScript(pid, stagedExe, primary, altTarget), Encoding.ASCII);
		Process.Start(new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = "/c \"" + scriptPath + "\"",
			UseShellExecute = false,
			CreateNoWindow = true
		});
	}
}
