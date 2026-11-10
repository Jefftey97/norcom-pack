using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public sealed class ZomboidWorkshopMod
{
	public string WorkshopId { get; init; } = "";

	public string ModId { get; init; } = "";
}

public sealed class ZomboidPackFile
{
	public string Path { get; init; } = "";

	public string Url { get; init; } = "";

	public string Sha256 { get; init; } = "";

	public long Size { get; init; }
}

public sealed class ZomboidFeedDoc
{
	public IReadOnlyList<ZomboidWorkshopMod> Workshop { get; init; } = Array.Empty<ZomboidWorkshopMod>();

	public IReadOnlyList<ZomboidPackFile> Files { get; init; } = Array.Empty<ZomboidPackFile>();

	public string Host { get; init; } = "";

	public int Port { get; init; }
}

public static class ZomboidFeed
{
	public const string Url = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/zomboid.json";

	public static ZomboidFeedDoc Parse(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new ZomboidFeedDoc();
		}
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		List<ZomboidWorkshopMod> workshop = new List<ZomboidWorkshopMod>();
		if (root.TryGetProperty("workshop", out JsonElement mods) && mods.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement mod in mods.EnumerateArray())
			{
				string workshopId = Text(mod, "workshopId");
				string modId = Text(mod, "modId");
				if (workshopId.Length == 0 || modId.Length == 0 || !Digits(workshopId))
				{
					continue;
				}
				workshop.Add(new ZomboidWorkshopMod { WorkshopId = workshopId, ModId = modId });
			}
		}
		List<ZomboidPackFile> files = new List<ZomboidPackFile>();
		if (root.TryGetProperty("files", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement file in listed.EnumerateArray())
			{
				files.Add(new ZomboidPackFile
				{
					Path = Text(file, "path").Replace('\\', '/'),
					Url = Text(file, "url"),
					Sha256 = Text(file, "sha256"),
					Size = file.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0L
				});
			}
		}
		string host = "";
		int port = 0;
		if (root.TryGetProperty("connect", out JsonElement connect) && connect.ValueKind == JsonValueKind.Object)
		{
			host = Text(connect, "host");
			if (connect.TryGetProperty("port", out JsonElement portValue) && portValue.TryGetInt32(out int parsed))
			{
				port = parsed;
			}
		}
		return new ZomboidFeedDoc
		{
			Workshop = workshop,
			Files = files,
			Host = host,
			Port = port
		};
	}

	public static string SteamUrl(ZomboidFeedDoc feed, string localPassword, out string note)
	{
		note = "";
		if (feed == null || !SafeHost(feed.Host) || feed.Port < 1 || feed.Port > 65535)
		{
			return ZomboidLaunch.SteamUrl;
		}
		string args = "+connect " + feed.Host + ":" + feed.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (!string.IsNullOrEmpty(localPassword))
		{
			if (!SafePassword(localPassword))
			{
				note = "The saved server password was not used.";
			}
			else
			{
				args += " +password " + localPassword;
			}
		}
		return ZomboidLaunch.SteamUrl + "//" + args.Replace(" ", "%20", StringComparison.Ordinal);
	}

	public static async Task SyncFilesAsync(HttpClient http, string zomboidRoot, ZomboidFeedDoc feed, CancellationToken ct)
	{
		if (feed == null)
		{
			return;
		}
		Directory.CreateDirectory(zomboidRoot);
		foreach (ZomboidPackFile file in feed.Files)
		{
			ct.ThrowIfCancellationRequested();
			string relative = (file.Path ?? "").Replace('\\', '/');
			if (!relative.StartsWith("mods/", StringComparison.Ordinal) || relative.Contains("..", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(file.Sha256))
			{
				throw new InvalidDataException("Zomboid file was refused: " + relative);
			}
			if (file.Url == null || !file.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Zomboid file was refused: " + relative);
			}
			string dest = PackSyncService.ConfinedPath(zomboidRoot, relative);
			if (File.Exists(dest) && (file.Size <= 0 || new FileInfo(dest).Length == file.Size) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, "sha256", ct).ConfigureAwait(false), file.Sha256))
			{
				continue;
			}
			string tempRelative = relative + "." + Guid.NewGuid().ToString("n") + ".norcom-partial";
			string temp = PackSyncService.ConfinedPath(zomboidRoot, tempRelative);
			await HttpDownload.DownloadAsync(http, file.Url, temp, new TransferMeter(), ct).ConfigureAwait(false);
			bool matches = (file.Size <= 0 || new FileInfo(temp).Length == file.Size) && Hashing.EqualsHex(await Hashing.FileHexAsync(temp, "sha256", ct).ConfigureAwait(false), file.Sha256);
			if (!matches)
			{
				File.Delete(temp);
				throw new InvalidDataException("Zomboid file hash does not match: " + relative);
			}
			PackSyncService.ConfinedPath(zomboidRoot, relative);
			string parent = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(parent))
			{
				Directory.CreateDirectory(parent);
			}
			if (File.Exists(dest))
			{
				File.Replace(temp, dest, null);
			}
			else
			{
				File.Move(temp, dest);
			}
		}
	}

	public static string WorkshopNotice(string launcherHome, string libraryPath, IReadOnlyList<ZomboidWorkshopMod> mods)
	{
		if (mods == null || mods.Count == 0)
		{
			return "";
		}
		List<string> missing = new List<string>();
		List<string> ids = new List<string>();
		for (int i = 0; i < mods.Count; i++)
		{
			ids.Add(mods[i].WorkshopId);
			string folder = string.IsNullOrEmpty(libraryPath) ? "" : SteamLibrary.Join(libraryPath, "steamapps", "workshop", "content", "108600", mods[i].WorkshopId);
			if (folder.Length == 0 || !Directory.Exists(folder))
			{
				missing.Add(mods[i].ModId + " (" + mods[i].WorkshopId + ")");
			}
		}
		if (missing.Count == 0)
		{
			return "";
		}
		ids.Sort(StringComparer.Ordinal);
		string signature = string.Join(",", ids);
		string marker = Path.Combine(launcherHome, "zomboid-workshop-seen.txt");
		try
		{
			if (File.Exists(marker) && File.ReadAllText(marker) == signature)
			{
				return "";
			}
			Directory.CreateDirectory(launcherHome);
			File.WriteAllText(marker, signature);
		}
		catch (IOException)
		{
		}
		return "Subscribe to these Workshop mods in Steam once: " + string.Join(", ", missing) + ". NORCOM will not ask again for this list.";
	}

	private static string Text(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString()?.Trim() ?? "";
	}

	private static bool Digits(string value)
	{
		if (value.Length == 0 || value.Length > 20)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] < '0' || value[i] > '9')
			{
				return false;
			}
		}
		return true;
	}

	private static bool SafeHost(string host)
	{
		if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Contains("rcon", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		for (int i = 0; i < host.Length; i++)
		{
			char c = host[i];
			if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-'))
			{
				return false;
			}
		}
		return true;
	}

	private static bool SafePassword(string password)
	{
		if (password.Length == 0 || password.Length > 64)
		{
			return false;
		}
		for (int i = 0; i < password.Length; i++)
		{
			char c = password[i];
			if (c <= ' ' || c == '%' || c == '"' || c == '\\' || c == '/' || c == '+')
			{
				return false;
			}
		}
		return true;
	}
}
