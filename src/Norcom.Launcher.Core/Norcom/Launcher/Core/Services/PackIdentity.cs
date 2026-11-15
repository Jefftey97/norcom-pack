using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class PackIdentity
{
	public const string FileName = "pack-identity.json";

	[JsonPropertyName("packVersion")]
	public string PackVersion { get; set; } = "";

	[JsonPropertyName("indexSha256")]
	public string IndexSha256 { get; set; } = "";

	public static string PathBeside(string managedPath)
	{
		string directory = Path.GetDirectoryName(managedPath);
		if (string.IsNullOrEmpty(directory))
		{
			directory = ".";
		}
		return Path.Combine(directory, FileName);
	}

	public static PackIdentity TryRead(string managedPath)
	{
		string path = PathBeside(managedPath);
		if (string.IsNullOrWhiteSpace(managedPath) || !File.Exists(path))
		{
			return null;
		}
		try
		{
			PackIdentity identity = JsonSerializer.Deserialize<PackIdentity>(File.ReadAllText(path));
			if (identity == null || string.IsNullOrWhiteSpace(identity.PackVersion) || string.IsNullOrWhiteSpace(identity.IndexSha256))
			{
				return null;
			}
			return identity;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static void ReadRemote(TomlLite.Doc doc, out string version, out string hashFormat, out string indexHash)
	{
		version = "";
		hashFormat = "sha256";
		indexHash = "";
		if (doc == null)
		{
			return;
		}
		version = (doc.Keys.GetValueOrDefault("version") ?? "").Trim();
		hashFormat = doc.Keys.GetValueOrDefault("index.hash-format") ?? doc.Keys.GetValueOrDefault("hash-format") ?? "sha256";
		indexHash = (doc.Keys.GetValueOrDefault("index.hash") ?? "").Trim();
	}

	public bool Matches(string packVersion, string hashFormat, string indexHash)
	{
		if (!string.Equals(hashFormat ?? "", "sha256", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return string.Equals(PackVersion, (packVersion ?? "").Trim(), StringComparison.Ordinal) && string.Equals(IndexSha256, (indexHash ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
	}

	public static void Record(string managedPath, string packVersion, string hashFormat, string indexHash)
	{
		if (!string.Equals(hashFormat ?? "", "sha256", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(packVersion) || string.IsNullOrWhiteSpace(indexHash))
		{
			return;
		}
		string path = PathBeside(managedPath);
		string directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string json = JsonSerializer.Serialize(new PackIdentity
		{
			PackVersion = packVersion.Trim(),
			IndexSha256 = indexHash.Trim().ToLowerInvariant()
		});
		string temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
		File.WriteAllText(temp, json);
		File.Move(temp, path, overwrite: true);
	}

	public static void Delete(string managedPath)
	{
		string path = PathBeside(managedPath);
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("pack identity " + ex.GetType().Name);
		}
	}

	public static bool IsUnreachable(Exception ex, CancellationToken ct)
	{
		if (ex is HttpRequestException)
		{
			return true;
		}
		return ex is TaskCanceledException && !ct.IsCancellationRequested;
	}
}
