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
		try
		{
			LauncherCleanup.BeforePlay();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup " + ex.GetType().Name);
		}
		meter.SetPhase("Reading pack", 0.02, 0.05);
		TomlLite.Doc doc = TomlLite.Parse(await _http.GetStringAsync(packUrl, ct).ConfigureAwait(continueOnCapturedContext: false));
		bool packSynced = HasSyncRecord(gameDir, managedPath);
		bool instancePresent = PrismInstance.HasNorcomInstance(PrismCommand.DefaultDataDir(NorcomPaths.AppData));
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
		if (string.IsNullOrWhiteSpace(indexHash))
		{
			throw new InvalidDataException("Pack index has no hash.");
		}
		await DownloadVerifiedAsync(cacheDir, url, indexPath, indexHash, indexFmt, "Pack index hash does not match pack.toml.", meter, ct).ConfigureAwait(continueOnCapturedContext: false);
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
				string dest = ConfinedPath(gameDir, destRel);
				if (preserve && File.Exists(dest))
				{
					managed.Add(new ManagedEntry(destRel, Preserve: true));
					continue;
				}
				if (string.IsNullOrWhiteSpace(hash))
				{
					throw new InvalidDataException("Pack file has no hash: " + rel);
				}
				if (File.Exists(dest) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fmt, ct).ConfigureAwait(continueOnCapturedContext: false), hash))
				{
					managed.Add(new ManagedEntry(destRel, preserve));
					continue;
				}
				string url2 = root + "/" + rel;
				await DownloadVerifiedAsync(gameDir, url2, dest, hash, fmt, "Hash mismatch for " + rel, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
				managed.Add(new ManagedEntry(destRel, preserve));
			}
			rel = null;
		}
		string home = Path.GetDirectoryName(managedPath) ?? gameDir;
		PackUpgrade.RetireSaved(Path.Combine(home, "Launcher"), gameDir, managed.Select(entry => entry.RelativePath).ToList(), PinGate.PublishedJarNames);
		string quarantineDir = Path.Combine(home, "recovery", "pack-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
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
		meter.HandOff();
		}, ct, packSynced, instancePresent).ConfigureAwait(continueOnCapturedContext: false);
		RequireMods(gameDir, managedPath);
	}

	// A pin stamp written by the pin poll is not a pack sync. The sync record is the managed manifest, and only after every mod jar is on disk.
	public static bool HasSyncRecord(string gameDir, string managedPath)
	{
		if (string.IsNullOrWhiteSpace(PinGate.ReadAppliedPin(gameDir)) || string.IsNullOrWhiteSpace(managedPath) || !File.Exists(managedPath))
		{
			return false;
		}
		return MissingMods(gameDir, managedPath).Count == 0;
	}

	public static void RequireMods(string gameDir, string managedPath)
	{
		List<string> missing = MissingMods(gameDir, managedPath);
		if (missing.Count == 0)
		{
			return;
		}
		throw new PinMismatchException("The friends pack is missing " + string.Join(", ", missing) + ", so Minecraft was not started.");
	}

	public static List<string> MissingMods(string gameDir, string managedPath)
	{
		List<string> missing = new List<string>();
		if (string.IsNullOrWhiteSpace(managedPath) || !File.Exists(managedPath))
		{
			missing.Add("the pack sync record");
			return missing;
		}
		List<ManagedEntry>? entries;
		try
		{
			entries = JsonSerializer.Deserialize<List<ManagedEntry>>(File.ReadAllText(managedPath));
		}
		catch (JsonException)
		{
			missing.Add("the pack sync record");
			return missing;
		}
		if (entries == null)
		{
			missing.Add("the pack sync record");
			return missing;
		}
		foreach (ManagedEntry entry in entries)
		{
			string rel = (entry.RelativePath ?? "").Replace('\\', '/');
			if (entry.Preserve || !rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) || !rel.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			try
			{
				if (!File.Exists(ConfinedPath(gameDir, rel)))
				{
					missing.Add(Path.GetFileName(rel));
				}
			}
			catch (InvalidDataException)
			{
				missing.Add(Path.GetFileName(rel));
			}
		}
		return missing;
	}

	private async Task<ManagedEntry?> InstallMetaAsync(string gameDir, string cacheDir, string root, string rel, string hash, string fmt, bool preserve, TransferMeter meter, CancellationToken ct)
	{
		string tomlDest = ConfinedPath(cacheDir, rel);
		if (string.IsNullOrWhiteSpace(hash))
		{
			throw new InvalidDataException("Pack file has no hash: " + rel);
		}
		bool flag = true;
		if (File.Exists(tomlDest))
		{
			flag = !Hashing.EqualsHex(await Hashing.FileHexAsync(tomlDest, fmt, ct).ConfigureAwait(continueOnCapturedContext: false), hash);
		}
		if (flag)
		{
			await DownloadVerifiedAsync(cacheDir, root + "/" + rel, tomlDest, hash, fmt, "Hash mismatch for " + rel, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
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
		string dest = ConfinedPath(gameDir, destRel);
		string fileHash = doc.Keys.GetValueOrDefault("download.hash") ?? doc.Keys.GetValueOrDefault("hash") ?? "";
		string fileFmt = doc.Keys.GetValueOrDefault("download.hash-format") ?? doc.Keys.GetValueOrDefault("hash-format") ?? "sha256";
		if (preserve && File.Exists(dest))
		{
			return new ManagedEntry(destRel, Preserve: true);
		}
		if (string.IsNullOrWhiteSpace(fileHash))
		{
			throw new InvalidDataException("Pack file has no hash: " + filename);
		}
		if (File.Exists(dest) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, fileFmt, ct).ConfigureAwait(continueOnCapturedContext: false), fileHash))
		{
			return new ManagedEntry(destRel, preserve);
		}
		meter.SetPhase("Downloading " + filename, meter.Snapshot().Fraction, 0.02);
		await DownloadVerifiedAsync(gameDir, url, dest, fileHash, fileFmt, "Hash mismatch for " + filename, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
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

	// Pack paths come from the unsigned main-branch index and .pw.toml files.
	// Writes stay inside the target folder, as IsSafeToDelete already requires for removals.
	public static string ConfinedPath(string baseDir, string relative)
	{
		if (string.IsNullOrWhiteSpace(relative))
		{
			throw new InvalidDataException("Pack file path is empty.");
		}
		string text = relative.Replace('\\', '/');
		if (Path.IsPathRooted(text) || text[0] == '/' || text.IndexOf(':') >= 0 || text.IndexOf('\0') >= 0)
		{
			throw new InvalidDataException("Pack file path leaves the game folder: " + relative);
		}
		string[] parts = text.Split('/');
			foreach (string part in parts)
		{
			if (part.Length == 0 || part == "." || part == ".." || part[part.Length - 1] == ' ' || part[part.Length - 1] == '.' || IsReservedDeviceName(part))
			{
				throw new InvalidDataException("Pack file path leaves the game folder: " + relative);
			}
			foreach (char c in part)
			{
				if (c < ' ')
				{
					throw new InvalidDataException("Pack file path leaves the game folder: " + relative);
				}
			}
		}
		string root = Path.GetFullPath(baseDir);
		string full = Path.GetFullPath(Path.Combine(root, text.Replace('/', Path.DirectorySeparatorChar)));
		if (!IsInsideDirectory(root, full))
		{
			throw new InvalidDataException("Pack file path leaves the game folder: " + relative);
		}
		// Existing symlinks and junctions are not visible to the string check above.
		RejectReparseEscape(root, full, createMissingParents: false);
		return full;
	}

	// Case-insensitive, and the directory prefix includes the separator so
	// C:\game does not accept C:\game2. GetFullPath does not resolve reparse points;
	// RejectReparseEscape does that before anything is written.
	public static bool IsInsideDirectory(string directoryPath, string candidatePath)
	{
		string root = Path.GetFullPath(directoryPath);
		string full = Path.GetFullPath(candidatePath);
		string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
		return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<bool> MatchesOnDiskAsync(string baseDir, string dest, string hash, string format, CancellationToken ct)
	{
		if (!File.Exists(dest))
		{
			return false;
		}
		RejectReparseEscape(baseDir, dest, createMissingParents: false);
		return Hashing.EqualsHex(await Hashing.FileHexAsync(dest, format, ct).ConfigureAwait(continueOnCapturedContext: false), hash);
	}

	private async Task DownloadVerifiedAsync(string baseDir, string url, string dest, string hash, string format, string mismatchMessage, TransferMeter meter, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(hash))
		{
			throw new InvalidDataException("Pack file has no hash.");
		}
		if (await MatchesOnDiskAsync(baseDir, dest, hash, format, ct).ConfigureAwait(continueOnCapturedContext: false))
		{
			LauncherLog.Write("pack skip " + Path.GetFileName(dest));
			return;
		}
		string temp = dest + "." + Guid.NewGuid().ToString("n") + ".norcom-partial";
		bool placed = false;
		try
		{
			await HttpDownload.DownloadAsync(_http, url, temp, meter, ct, delegate
			{
				// Re-check immediately before the parent is created, the .part file is opened, and the partial is moved.
				RejectReparseEscape(baseDir, temp, createMissingParents: true);
				RejectReparseEscape(baseDir, temp + ".part", createMissingParents: false);
			}).ConfigureAwait(continueOnCapturedContext: false);
			RejectReparseEscape(baseDir, temp, createMissingParents: false);
			if (!Hashing.EqualsHex(await Hashing.FileHexAsync(temp, format, ct).ConfigureAwait(continueOnCapturedContext: false), hash))
			{
				throw new InvalidDataException(mismatchMessage);
			}
			RejectReparseEscape(baseDir, dest, createMissingParents: true);
			RejectReparseEscape(baseDir, temp, createMissingParents: false);
			File.Move(temp, dest, overwrite: true);
			placed = true;
		}
		finally
		{
			if (!placed)
			{
				DeleteQuiet(baseDir, temp);
				DeleteQuiet(baseDir, temp + ".part");
			}
		}
	}

	// baseDir may itself be a junction. Friends sometimes relocate the game folder that way, and writes through that root are the relocated folder.
	// Every existing component below baseDir, including the destination file, must be a real directory or a real file. Missing parents are created one level at a time and checked again.
	// A symlink or junction at the destination is refused. It is not deleted and it is not followed.
	private static void RejectReparseEscape(string baseDir, string dest, bool createMissingParents)
	{
		string root = Path.GetFullPath(baseDir);
		string full = Path.GetFullPath(dest);
		if (!IsInsideDirectory(root, full))
		{
			throw new InvalidDataException("Pack file path leaves the game folder: " + dest);
		}
		string relative = Path.GetRelativePath(root, full);
		if (relative.Length == 0 || relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("Pack file path leaves the game folder: " + dest);
		}
		string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string current = root;
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i].Length == 0 || parts[i] == "." || parts[i] == "..")
			{
				throw new InvalidDataException("Pack file path leaves the game folder: " + dest);
			}
			current = Path.Combine(current, parts[i]);
			bool leaf = i == parts.Length - 1;
			if (!EntryExists(current))
			{
				if (leaf || !createMissingParents)
				{
					return;
				}
				Directory.CreateDirectory(current);
			}
			if (IsReparsePoint(current))
			{
				throw new InvalidDataException("Pack file path leaves the game folder: " + dest);
			}
			if (!leaf && (File.GetAttributes(current) & FileAttributes.Directory) == 0)
			{
				throw new InvalidDataException("Pack file path leaves the game folder: " + dest);
			}
		}
	}

	// Windows opens these names, with or without an extension, as devices rather than as files in the folder.
	// The current pack has none of them. auxiliary.jar and comet.jar are ordinary names and stay allowed.
	private static bool IsReservedDeviceName(string segment)
	{
		int dot = segment.IndexOf('.');
		string name = dot >= 0 ? segment.Substring(0, dot) : segment;
		if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (name.Length != 4)
		{
			return false;
		}
		if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		char digit = name[3];
		return digit >= '1' && digit <= '9';
	}

	private static bool EntryExists(string path)
	{
		try
		{
			File.GetAttributes(path);
			return true;
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return false;
		}
	}

	private static bool IsReparsePoint(string path)
	{
		FileAttributes attributes;
		try
		{
			attributes = File.GetAttributes(path);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return false;
		}
		if ((attributes & FileAttributes.ReparsePoint) != 0)
		{
			return true;
		}
		FileSystemInfo link = null;
		try
		{
			link = File.ResolveLinkTarget(path, returnFinalTarget: false);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
		{
			link = null;
		}
		if (link != null)
		{
			return true;
		}
		return !string.IsNullOrEmpty(new FileInfo(path).LinkTarget);
	}

	public static void WriteContainedText(string baseDir, string relative, string contents)
	{
		string dest = ConfinedPath(baseDir, relative);
		RejectReparseEscape(baseDir, dest, createMissingParents: true);
		string text = relative.Replace('\\', '/');
		int slash = text.LastIndexOf('/');
		string tempRelative = (slash >= 0 ? text.Substring(0, slash + 1) : "") + Path.GetFileName(dest) + "." + Guid.NewGuid().ToString("n") + ".norcom-partial";
		string temp = ConfinedPath(baseDir, tempRelative);
		RejectReparseEscape(baseDir, temp, createMissingParents: false);
		try
		{
			using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				byte[] bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents ?? "");
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush(true);
			}
			RejectReparseEscape(baseDir, dest, createMissingParents: false);
			if (EntryExists(dest))
			{
				File.Replace(temp, dest, null);
			}
			else
			{
				File.Move(temp, dest);
			}
		}
		finally
		{
			DeleteQuiet(baseDir, temp);
		}
	}

	private static void DeleteQuiet(string baseDir, string path)
	{
		try
		{
			// A link below the folder root is left alone. Deleting it can follow the link, and the root junction itself is allowed.
			RejectReparseEscape(baseDir, path, createMissingParents: false);
			if (!EntryExists(path) || IsReparsePoint(path))
			{
				return;
			}
			if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
			{
				return;
			}
			File.Delete(path);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pack partial " + ex.GetType().Name);
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
