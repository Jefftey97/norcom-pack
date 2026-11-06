using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class PackSyncService
{
	private readonly HttpClient _http;

	public PackSyncService(HttpClient http)
	{
		_http = http;
	}

	public async Task SyncAsync(string gameDir, string managedPath, string cacheDir, string packUrl, TransferMeter meter, CancellationToken ct)
	{
		meter.SetPhase("Reading pack", 0.02, 0.05);
		TomlLite.Doc doc = TomlLite.Parse(await _http.GetStringAsync(packUrl, ct).ConfigureAwait(continueOnCapturedContext: false));
		await PinHeal.RunAsync(_http, gameDir, doc, packUrl, async _ =>
		{
		meter.SetPhase("Repair: resyncing the pack", 0.08, 0.8);
		string value;
		string text = (doc.Keys.TryGetValue("index.file", out value) ? value : "index.toml");
		string indexHash = doc.Keys.GetValueOrDefault("index.hash") ?? "";
		string indexFmt = doc.Keys.GetValueOrDefault("index.hash-format") ?? doc.Keys.GetValueOrDefault("hash-format") ?? "sha256";
		string root = packUrl.Substring(0, packUrl.LastIndexOf('/'));
		string url = root + "/" + text.TrimStart('/');
		string indexPath = Path.Combine(cacheDir, "index.toml");
		Directory.CreateDirectory(cacheDir);
		meter.SetPhase("Pack index", 0.06, 0.06);
		await HttpDownload.DownloadAsync(_http, url, indexPath, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrWhiteSpace(indexHash) && !Hashing.EqualsHex(await Hashing.FileHexAsync(indexPath, indexFmt, ct).ConfigureAwait(continueOnCapturedContext: false), indexHash))
		{
			throw new InvalidDataException("Pack index hash does not match pack.toml.");
		}
		TomlLite.Doc index = TomlLite.Parse(await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(continueOnCapturedContext: false));
		List<ManagedEntry> managed = new List<ManagedEntry>();
		int total = Math.Max(1, index.Files.Count);
		int done = 0;
		foreach (Dictionary<string, string> file in index.Files)
		{
			ct.ThrowIfCancellationRequested();
			done++;
			if (!file.TryGetValue("file", out var rel) || string.IsNullOrWhiteSpace(rel))
			{
				continue;
			}
			rel = rel.Replace('\\', '/');
			string value2;
			bool preserve = file.TryGetValue("preserve", out value2) && value2.Equals("true", StringComparison.OrdinalIgnoreCase);
			string hash = file.GetValueOrDefault("hash") ?? "";
			string fmt = file.GetValueOrDefault("hash-format") ?? index.Keys.GetValueOrDefault("hash-format") ?? "sha256";
			meter.SetPhase($"Pack {done}/{total}  {Path.GetFileName(rel)}", 0.12 + 0.78 * ((double)done / (double)total), 0.01);
			if (rel.EndsWith(".pw.toml", StringComparison.OrdinalIgnoreCase))
			{
				ManagedEntry managedEntry = await InstallMetaAsync(gameDir, cacheDir, root, rel, hash, fmt, preserve, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)managedEntry != null)
				{
					managed.Add(managedEntry);
				}
			}
			else
			{
				string destRel = rel;
				string dest = Path.Combine(gameDir, destRel.Replace('/', Path.DirectorySeparatorChar));
				if (preserve && File.Exists(dest))
				{
					managed.Add(new ManagedEntry(destRel, Preserve: true));
					continue;
				}
				if (File.Exists(dest) && !string.IsNullOrWhiteSpace(hash) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fmt, ct).ConfigureAwait(continueOnCapturedContext: false), hash))
				{
					managed.Add(new ManagedEntry(destRel, preserve));
					continue;
				}
				string url2 = root + "/" + rel;
				await HttpDownload.DownloadAsync(_http, url2, dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!string.IsNullOrWhiteSpace(hash) && !Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fmt, ct).ConfigureAwait(continueOnCapturedContext: false), hash))
				{
					throw new InvalidDataException("Hash mismatch for " + rel);
				}
				managed.Add(new ManagedEntry(destRel, preserve));
			}
			rel = null;
		}
		string quarantineDir = Path.Combine(Path.GetDirectoryName(managedPath) ?? gameDir, "recovery", "pack-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
		if (File.Exists(managedPath))
		{
			QuarantinePrevious(gameDir, quarantineDir, managedPath, managed);
		}
		else
		{
			QuarantineLegacyPackwiz(gameDir, quarantineDir, managed);
		}
		string directoryName = Path.GetDirectoryName(managedPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		await File.WriteAllTextAsync(managedPath, JsonSerializer.Serialize(managed, new JsonSerializerOptions
		{
			WriteIndented = true
		}), ct).ConfigureAwait(continueOnCapturedContext: false);
		meter.SetPhase("Pack ready", 0.92, 0.0);
		LauncherLog.Write("Pack sync complete files=" + managed.Count);
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<ManagedEntry?> InstallMetaAsync(string gameDir, string cacheDir, string root, string rel, string hash, string fmt, bool preserve, TransferMeter meter, CancellationToken ct)
	{
		string tomlDest = Path.Combine(cacheDir, rel.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(tomlDest));
		bool flag = true;
		if (File.Exists(tomlDest) && !string.IsNullOrWhiteSpace(hash))
		{
			flag = !Hashing.EqualsHex(await Hashing.FileHexAsync(tomlDest, fmt, ct).ConfigureAwait(continueOnCapturedContext: false), hash);
		}
		if (flag)
		{
			await HttpDownload.DownloadAsync(_http, root + "/" + rel, tomlDest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		TomlLite.Doc doc = TomlLite.Parse(await File.ReadAllTextAsync(tomlDest, ct).ConfigureAwait(continueOnCapturedContext: false));
		if ((doc.Keys.GetValueOrDefault("side") ?? "both").Equals("server", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string text = doc.Keys.GetValueOrDefault("option.optional") ?? doc.Keys.GetValueOrDefault("optional");
		string text2 = doc.Keys.GetValueOrDefault("option.default") ?? doc.Keys.GetValueOrDefault("default");
		if (text != null && text.Equals("true", StringComparison.OrdinalIgnoreCase) && text2 != null && text2.Equals("false", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		string filename = doc.Keys.GetValueOrDefault("filename");
		string url = doc.Keys.GetValueOrDefault("download.url") ?? doc.Keys.GetValueOrDefault("url");
		if (string.IsNullOrWhiteSpace(filename) || string.IsNullOrWhiteSpace(url))
		{
			throw new InvalidDataException("Pack file missing download: " + rel);
		}
		string path = Path.GetDirectoryName(rel.Replace('/', Path.DirectorySeparatorChar)) ?? "";
		string destRel = Path.Combine(path, filename).Replace('\\', '/');
		string dest = Path.Combine(gameDir, destRel.Replace('/', Path.DirectorySeparatorChar));
		string fileHash = doc.Keys.GetValueOrDefault("download.hash") ?? doc.Keys.GetValueOrDefault("hash") ?? "";
		string fileFmt = doc.Keys.GetValueOrDefault("download.hash-format") ?? doc.Keys.GetValueOrDefault("hash-format") ?? "sha256";
		if (preserve && File.Exists(dest))
		{
			return new ManagedEntry(destRel, Preserve: true);
		}
		if (File.Exists(dest) && !string.IsNullOrWhiteSpace(fileHash) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fileFmt, ct).ConfigureAwait(continueOnCapturedContext: false), fileHash))
		{
			return new ManagedEntry(destRel, preserve);
		}
		meter.SetPhase("Downloading " + filename, meter.Snapshot().Fraction, 0.02);
		await HttpDownload.DownloadAsync(_http, url, dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrWhiteSpace(fileHash) && !Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fileFmt, ct).ConfigureAwait(continueOnCapturedContext: false), fileHash))
		{
			throw new InvalidDataException("Hash mismatch for " + filename);
		}
		return new ManagedEntry(destRel, preserve);
	}

	public static IReadOnlyList<string> QuarantinePrevious(string gameDir, string quarantineDir, string managedPath, IReadOnlyList<ManagedEntry> next)
	{
		List<ManagedEntry> list;
		try
		{
			list = JsonSerializer.Deserialize<List<ManagedEntry>>(File.ReadAllText(managedPath));
		}
		catch
		{
			return Array.Empty<string>();
		}
		if (list == null)
		{
			return Array.Empty<string>();
		}
		HashSet<string> hashSet = next.Select((ManagedEntry m) => m.RelativePath.Replace('\\', '/')).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list2 = new List<string>();
		foreach (ManagedEntry item in list)
		{
			string text = item.RelativePath.Replace('\\', '/');
			if (!hashSet.Contains(text) && !item.Preserve && TryQuarantine(gameDir, quarantineDir, text))
			{
				list2.Add(text);
			}
		}
		WriteNote(quarantineDir, list2);
		return list2;
	}

	public static IReadOnlyList<string> QuarantineLegacyPackwiz(string gameDir, string quarantineDir, IReadOnlyList<ManagedEntry> next)
	{
		HashSet<string> hashSet = next.Select((ManagedEntry m) => m.RelativePath.Replace('\\', '/')).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		string[] array = new string[3] { "mods", "resourcepacks", "shaderpacks" };
		foreach (string path in array)
		{
			string path2 = Path.Combine(gameDir, path);
			if (!Directory.Exists(path2))
			{
				continue;
			}
			foreach (string item in Directory.EnumerateFiles(path2, "*.pw.toml", SearchOption.AllDirectories))
			{
				if (!TomlLite.Parse(File.ReadAllText(item)).Keys.TryGetValue("filename", out string value) || string.IsNullOrWhiteSpace(value))
				{
					continue;
				}
				string text = Path.GetRelativePath(gameDir, item).Replace('\\', '/');
				string text2 = Path.GetRelativePath(gameDir, Path.Combine(Path.GetDirectoryName(item), value)).Replace('\\', '/');
				if (!hashSet.Contains(text2))
				{
					if (TryQuarantine(gameDir, quarantineDir, text2))
					{
						list.Add(text2);
					}
					if (TryQuarantine(gameDir, quarantineDir, text))
					{
						list.Add(text);
					}
				}
			}
		}
		WriteNote(quarantineDir, list);
		return list;
	}

	private static bool TryQuarantine(string gameDir, string quarantineDir, string relative)
	{
		string text = relative.Replace('\\', '/');
		if (!IsSafeToDelete(text))
		{
			return false;
		}
		string text2 = Path.Combine(gameDir, text.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(text2))
		{
			return false;
		}
		string text3 = Path.Combine(quarantineDir, text.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(text3));
		if (File.Exists(text3))
		{
			text3 += ".dup";
		}
		try
		{
			File.Move(text2, text3);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void WriteNote(string quarantineDir, IReadOnlyList<string> moved)
	{
		if (moved.Count != 0)
		{
			Directory.CreateDirectory(quarantineDir);
			File.WriteAllText(Path.Combine(quarantineDir, "README.txt"), "These files were removed from the NORCOM game folder because a packwiz .pw.toml or an earlier NORCOM sync said the pack installed them, and the current pack no longer includes them." + Environment.NewLine + "Worlds, screenshots, servers.dat, options.txt, and jars with no packwiz metadata were not moved." + Environment.NewLine + "To put a file back, copy it into the same relative path under the game folder." + Environment.NewLine + string.Join(Environment.NewLine, moved) + Environment.NewLine);
		}
	}

	public static bool IsSafeToDelete(string relative)
	{
		string text = relative.Replace('\\', '/');
		if (text.Contains("..", StringComparison.Ordinal))
		{
			return false;
		}
		string[] array = new string[6] { "options.txt", "servers.dat", "saves/", "screenshots/", "norcom-skins/", "logs/" };
		foreach (string text2 in array)
		{
			if (text.Equals(text2.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) || text.StartsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		if (!text.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("resourcepacks/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("shaderpacks/", StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith("config/", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
