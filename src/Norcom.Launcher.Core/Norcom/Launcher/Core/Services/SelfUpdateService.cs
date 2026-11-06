using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class SelfUpdateService
{
	private readonly HttpClient _http;

	public const string ManifestOverrideVariable = "NORCOM_UPDATE_MANIFEST";

	public bool UsedPrivateManifest { get; private set; }

	public SelfUpdateService(HttpClient http)
	{
		_http = http;
	}

	public async Task<LauncherManifest?> FetchManifestAsync(CancellationToken ct)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("NORCOM_UPDATE_MANIFEST");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			UsedPrivateManifest = true;
			return await FetchPrivateAsync(environmentVariable.Trim(), ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		string[] array = new string[3] { "https://api.github.com/repos/Jefftey97/norcom-pack/contents/launcher/manifest.json?ref=main", "https://cdn.jsdelivr.net/gh/Jefftey97/norcom-pack@main/launcher/manifest.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/refs/heads/main/launcher/manifest.json" };
		string[] array2 = array;
		foreach (string url in array2)
		{
			try
			{
				using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
				{
					if (url.Contains("api.github.com", StringComparison.Ordinal))
					{
						req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
					}
					using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (!resp.IsSuccessStatusCode)
					{
						goto end_IL_01da;
					}
					string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
					string text2 = (url.Contains("api.github.com", StringComparison.Ordinal) ? DecodeGithubContent(text) : text);
					if (string.IsNullOrWhiteSpace(text2))
					{
						goto end_IL_01da;
					}
					text2 = text2.Trim().TrimStart('\ufeff');
					LauncherManifest launcherManifest = JsonSerializer.Deserialize<LauncherManifest>(text2);
					if (launcherManifest != null && !string.IsNullOrWhiteSpace(launcherManifest.Version))
					{
						UpdateNotes.Remember(NorcomPaths.LauncherDir, launcherManifest, NorcomVersion.Current);
						return launcherManifest;
					}
					goto end_IL_00ff;
					end_IL_01da:;
				}
				end_IL_00ff:;
			}
			catch (Exception ex)
			{
				LauncherLog.Write("Manifest fetch failed: " + ex.Message);
			}
		}
		return null;
	}

	public static bool IsAllowedPackageUrl(string? zipUrl, bool allowLocalFile)
	{
		if (string.IsNullOrWhiteSpace(zipUrl))
		{
			return false;
		}
		if (zipUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (allowLocalFile && Path.IsPathRooted(zipUrl))
		{
			return File.Exists(zipUrl);
		}
		return false;
	}

	public static UpdateKind Decide(string local, LauncherManifest manifest, bool allowLocalFile = false)
	{
		if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.ZipUrl) || string.IsNullOrWhiteSpace(manifest.Sha256))
		{
			return UpdateKind.None;
		}
		if (!IsAllowedPackageUrl(manifest.ZipUrl, allowLocalFile))
		{
			return UpdateKind.None;
		}
		if (manifest.Sha256.Trim().Length != 64)
		{
			return UpdateKind.None;
		}
		bool flag = VersionCompare.Compare(manifest.Version, local) > 0;
		if (!string.IsNullOrWhiteSpace(manifest.MinVersion) && VersionCompare.Compare(local, manifest.MinVersion) < 0 && VersionCompare.Compare(manifest.Version, local) >= 0)
		{
			return UpdateKind.Mandatory;
		}
		if (flag)
		{
			return UpdateKind.Available;
		}
		return UpdateKind.None;
	}

	public static UpdatePackageKind Inspect(string zipPath)
	{
		using ZipArchive zipArchive = ZipFile.OpenRead(zipPath);
		List<string> source = zipArchive.Entries.Select((ZipArchiveEntry e) => e.FullName.Replace('\\', '/')).ToList();
		if (source.Any((string n) => n.EndsWith("NorcomLauncher.exe", StringComparison.OrdinalIgnoreCase)))
		{
			return UpdatePackageKind.NativeExe;
		}
		if (source.Any((string n) => n.EndsWith("NORCOM-Launcher.ps1", StringComparison.OrdinalIgnoreCase)))
		{
			return UpdatePackageKind.LegacyBundle;
		}
		return UpdatePackageKind.Unknown;
	}

	public async Task<string> DownloadVerifiedAsync(LauncherManifest manifest, TransferMeter meter, CancellationToken ct)
	{
		HealHint hint = await PinHeal.FetchHintAsync(_http, ct).ConfigureAwait(continueOnCapturedContext: false);
		bool newer = !string.IsNullOrWhiteSpace(manifest.Version) && VersionCompare.Compare(manifest.Version, NorcomVersion.Current) > 0;
		if (newer || PinHeal.NeedsUpdate(NorcomVersion.Current, null, hint))
		{
			LauncherLog.Write("pin feed update first version=" + manifest.Version + " minLauncher=" + hint.MinLauncher);
		}
		else
		{
			await PinGate.EnforceFeedAsync(_http, NorcomPaths.GameDir, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		string text = Path.Combine(Path.GetTempPath(), "norcom-launcher-upd-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		string zip = Path.Combine(text, "update.zip");
		meter.SetPhase("Downloading launcher " + manifest.Version, 0.05, 0.8);
		if (!File.Exists(manifest.ZipUrl))
		{
			await HttpDownload.DownloadAsync(_http, manifest.ZipUrl, zip, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			File.Copy(manifest.ZipUrl, zip, overwrite: true);
		}
		if (!Hashing.EqualsHex(await Hashing.FileHexAsync(zip, "sha256", ct).ConfigureAwait(continueOnCapturedContext: false), manifest.Sha256))
		{
			throw new UpdateRejectedException("rejected hash-mismatch");
		}
		return zip;
	}

	public static string? ReadPayloadVersion(string exePath)
	{
		string directoryName = Path.GetDirectoryName(exePath);
		int num = 0;
		while (directoryName != null && num < 3)
		{
			string path = Path.Combine(directoryName, "payload-version.txt");
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path).Trim();
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
			directoryName = Path.GetDirectoryName(directoryName);
			num++;
		}
		return null;
	}

	public static string? FindNativeExe(string zipPath)
	{
		string text = Path.Combine(Path.GetDirectoryName(zipPath), "extract");
		ZipFile.ExtractToDirectory(zipPath, text, overwriteFiles: true);
		return Directory.EnumerateFiles(text, "NorcomLauncher.exe", SearchOption.AllDirectories).FirstOrDefault();
	}

	public static string BackupPathFor(string recoveryRoot, string version)
	{
		return Path.Combine(recoveryRoot, "self-update-" + Sanitize(version), "NorcomLauncher.previous.exe");
	}

	public static string BuildApplyScript(string newExe, string currentExe, int pid, string version, string backupExe, string logPath, string statusPath, string appliedShaPath, string sha256, bool failReplace = false)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("@echo off");
		stringBuilder.AppendLine("setlocal");
		stringBuilder.AppendLine(":wait");
		stringBuilder.Append("tasklist /FI \"PID eq ").Append(pid).AppendLine("\" | findstr /I \"" + pid + "\" >nul");
		stringBuilder.AppendLine("if not errorlevel 1 (");
		stringBuilder.AppendLine("  timeout /t 1 /nobreak >nul");
		stringBuilder.AppendLine("  goto wait");
		stringBuilder.AppendLine(")");
		stringBuilder.Append("if not exist \"").Append(backupExe).AppendLine("\" mkdir \"" + Path.GetDirectoryName(backupExe) + "\" >nul 2>&1");
		stringBuilder.AppendLine("set TRY=0");
		stringBuilder.AppendLine(":backup");
		stringBuilder.Append("if not exist \"").Append(currentExe).AppendLine("\" goto swap");
		stringBuilder.Append("copy /Y \"").Append(currentExe).Append("\" \"")
			.Append(backupExe)
			.AppendLine("\" >nul");
		stringBuilder.AppendLine("if errorlevel 1 (");
		stringBuilder.AppendLine("  set /a TRY+=1");
		stringBuilder.AppendLine("  if %TRY% GEQ 20 goto failed");
		stringBuilder.AppendLine("  timeout /t 1 /nobreak >nul");
		stringBuilder.AppendLine("  goto backup");
		stringBuilder.AppendLine(")");
		if (failReplace)
		{
			stringBuilder.AppendLine("goto rollback");
		}
		stringBuilder.AppendLine("set TRY=0");
		stringBuilder.AppendLine(":swap");
		stringBuilder.Append("copy /Y \"").Append(newExe).Append("\" \"")
			.Append(currentExe)
			.AppendLine("\" >nul");
		stringBuilder.AppendLine("if errorlevel 1 (");
		stringBuilder.AppendLine("  set /a TRY+=1");
		stringBuilder.AppendLine("  if %TRY% GEQ 20 goto rollback");
		stringBuilder.AppendLine("  timeout /t 1 /nobreak >nul");
		stringBuilder.AppendLine("  goto swap");
		stringBuilder.AppendLine(")");
		stringBuilder.Append("echo updated to ").Append(version).Append(">>\"")
			.Append(logPath)
			.AppendLine("\"");
		stringBuilder.Append("echo ").Append(UpdateReport.Applied(version)).Append(">\"")
			.Append(statusPath)
			.AppendLine("\"");
		stringBuilder.Append("echo ").Append(sha256).Append(">\"")
			.Append(appliedShaPath)
			.AppendLine("\"");
		stringBuilder.Append("start \"\" \"").Append(currentExe).AppendLine("\"");
		stringBuilder.AppendLine("exit /b 0");
		stringBuilder.AppendLine(":rollback");
		stringBuilder.Append("copy /Y \"").Append(backupExe).Append("\" \"")
			.Append(currentExe)
			.AppendLine("\" >nul");
		stringBuilder.Append("echo ").Append("rolled-back").Append(">\"")
			.Append(statusPath)
			.AppendLine("\"");
		stringBuilder.Append("echo rolled back ").Append(version).Append(">>\"")
			.Append(logPath)
			.AppendLine("\"");
		stringBuilder.AppendLine("set NORCOM_QA_FAIL_REPLACE=");
		stringBuilder.AppendLine("set NORCOM_UPDATE_MANIFEST=");
		stringBuilder.Append("start \"\" \"").Append(currentExe).AppendLine("\"");
		stringBuilder.AppendLine("exit /b 1");
		stringBuilder.AppendLine(":failed");
		stringBuilder.Append("echo ").Append("rolled-back").Append(">\"")
			.Append(statusPath)
			.AppendLine("\"");
		stringBuilder.Append("echo update failed before replace ").Append(version).Append(">>\"")
			.Append(logPath)
			.AppendLine("\"");
		stringBuilder.AppendLine("set NORCOM_QA_FAIL_REPLACE=");
		stringBuilder.AppendLine("set NORCOM_UPDATE_MANIFEST=");
		stringBuilder.Append("start \"\" \"").Append(currentExe).AppendLine("\"");
		stringBuilder.AppendLine("exit /b 1");
		return stringBuilder.ToString();
	}

	public static void ScheduleReplace(string newExe, string currentExe, int pid, string version, string sha256)
	{
		string text = BackupPathFor(NorcomPaths.RecoveryRoot, version);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		Directory.CreateDirectory(NorcomPaths.LauncherDir);
		string text2 = Path.Combine(Path.GetTempPath(), "norcom-apply-" + Guid.NewGuid().ToString("N") + ".cmd");
		string statusPath = Path.Combine(NorcomPaths.LauncherDir, "update-status.txt");
		string appliedShaPath = Path.Combine(NorcomPaths.LauncherDir, "update-applied.sha");
		File.WriteAllText(text2, BuildApplyScript(newExe, currentExe, pid, version, text, NorcomPaths.LogPath, statusPath, appliedShaPath, sha256.Trim().ToLowerInvariant(), QaGates.FailReplaceRequested()), Encoding.ASCII);
		Process.Start(new ProcessStartInfo
		{
			FileName = "cmd.exe",
			Arguments = "/c \"" + text2 + "\"",
			UseShellExecute = false,
			CreateNoWindow = true
		});
	}

	private async Task<LauncherManifest?> FetchPrivateAsync(string forced, CancellationToken ct)
	{
		_ = 2;
		try
		{
			string text;
			if (File.Exists(forced))
			{
				text = await File.ReadAllTextAsync(forced, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				if (!forced.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				using HttpResponseMessage resp = await _http.GetAsync(forced, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!resp.IsSuccessStatusCode)
				{
					return null;
				}
				text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			LauncherManifest launcherManifest = JsonSerializer.Deserialize<LauncherManifest>(text.Trim().TrimStart('\ufeff'));
			return (launcherManifest != null && !string.IsNullOrWhiteSpace(launcherManifest.Version)) ? launcherManifest : null;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("Private manifest skipped: " + ex.GetType().Name);
			return null;
		}
	}

	private static string Sanitize(string version)
	{
		string text = new string(version.Select(delegate(char c)
		{
			bool flag = char.IsLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == '-' || c == '.') ? true : false);
				flag = flag2;
			}
			return (!flag) ? '-' : c;
		}).ToArray()).Trim('-');
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "update";
	}

	private static string? DecodeGithubContent(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (!jsonDocument.RootElement.TryGetProperty("content", out var value))
			{
				return null;
			}
			byte[] bytes = Convert.FromBase64String(value.GetString()?.Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal) ?? "");
			return Encoding.UTF8.GetString(bytes);
		}
		catch
		{
			return null;
		}
	}
}
