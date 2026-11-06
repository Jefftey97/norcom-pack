using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class PinMismatchException : InvalidOperationException
{
	public PinMismatchException(string message)
		: base(message)
	{
	}
}

public static class PinGate
{
	public const string FriendsPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/norcom-mod-pin.json";

	public const string HostPinPath = "G:\\MinecraftServer\\admin\\norcom-mod-pin.json";

	public const string BlockCopy = "Pack doesn't match live server pin — wait for update. Repair before you play.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public const string DefaultPackUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string RequireVariable = "NORCOM_PIN_REQUIRED";

	public static string? LastWarning { get; private set; }

	public static string? AcceptedPinJson { get; private set; }

	public static bool MissingRefuses => IsOn(Environment.GetEnvironmentVariable("NORCOM_PIN_REQUIRED"));

	public static async Task EnforceFeedAsync(HttpClient http, string gameDir, CancellationToken ct)
	{
		string text = Environment.GetEnvironmentVariable("NORCOM_PACK_URL");
		string packUrl = string.IsNullOrWhiteSpace(text) ? DefaultPackUrl : text.Trim();
		string body;
		try
		{
			body = await http.GetStringAsync(packUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			if (MissingRefuses)
			{
				LauncherLog.Write("pin pack.toml " + ex.GetType().Name);
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pack.toml " + ex.GetType().Name);
			return;
		}
		await EnforceAsync(http, gameDir, TomlLite.Parse(body), ct, packUrl, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct)
	{
		return EnforceAsync(http, gameDir, pack, ct, null, null);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl, bool missingBlocks = true)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		string? host = HostDocument();
		if (!string.IsNullOrWhiteSpace(host))
		{
			Judge(ParseDocument(host), gameDir, pack, HostPinPath);
			return;
		}
		foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
		{
			string? body = await TryFetchTextAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (body == null)
			{
				continue;
			}
			Judge(ParseDocument(body), gameDir, pack, pinUrl);
			return;
		}
		if (MissingRefuses)
		{
			LauncherLog.Write("pin missing required");
			throw new PinMismatchException(BlockCopy);
		}
		Warn("pin 404");
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		string? body = await TryFetchTextAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (body == null)
		{
			if (MissingRefuses)
			{
				LauncherLog.Write("pin missing required " + pinUrl);
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pin 404 " + pinUrl);
			return;
		}
		Judge(ParseDocument(body), gameDir, pack, pinUrl);
	}

	public static IReadOnlyList<string> CandidateUrls(string? packUrl, string? manifestUrl)
	{
		List<string> urls = new List<string>();
		void Add(string? url)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				return;
			}
			string text = url.Trim();
			if (!urls.Contains(text, StringComparer.OrdinalIgnoreCase))
			{
				urls.Add(text);
			}
		}
		Add(Environment.GetEnvironmentVariable("NORCOM_PIN_URL"));
		Add(FriendsPinUrl);
		Add(Sibling(packUrl, "norcom-mod-pin.json"));
		Add(Sibling(manifestUrl, "norcom-mod-pin.json"));
		return urls;
	}

	public static string Rollup(IEnumerable<(string Name, string Sha256, long Size)> jars)
	{
		List<(string Name, string Sha256, long Size)> list = jars.OrderBy(jar => jar.Name, StringComparer.OrdinalIgnoreCase).ToList();
		StringBuilder builder = new StringBuilder();
		builder.Append('[');
		for (int i = 0; i < list.Count; i++)
		{
			if (i > 0)
			{
				builder.Append(',');
			}
			(string name, string sha, long size) = list[i];
			builder.Append("{\"name\":").Append(JsonSerializer.Serialize(name));
			builder.Append(",\"sha256\":").Append(JsonSerializer.Serialize(sha));
			builder.Append(",\"size\":").Append(size);
			builder.Append('}');
		}
		builder.Append(']');
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	public static bool IsClientOnly(string name)
	{
		string path = (name ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
		if (path.Contains("/shaderpacks/") || path.Contains("/resourcepacks/") || path.StartsWith("shaderpacks/", StringComparison.Ordinal) || path.StartsWith("resourcepacks/", StringComparison.Ordinal))
		{
			return true;
		}
		string id = Norm(Path.GetFileName(path));
		return id.Contains("sodium") || id.Contains("iris") || id.Contains("inventoryprofiles") || id.Contains("roughlyenoughitems") || id.Contains("distanthorizons");
	}

	private static void Judge(JsonDocument document, string gameDir, TomlLite.Doc pack, string source)
	{
		using (document)
		{
			JsonElement root = document.RootElement;
			string minecraft = Required(root, "minecraft");
			string loader = Required(root, "fabric_loader");
			string pinSha = Required(root, "pin_sha256");
			if (!root.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array)
			{
				throw new PinMismatchException("Server pin could not be read. Play was not started.");
			}
			List<(string Name, string Sha256, long Size)> parsed = new List<(string, string, long)>();
			foreach (JsonElement jar in jars.EnumerateArray())
			{
				string name = Required(jar, "name");
				string sha = Required(jar, "sha256");
				if (!jar.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes))
				{
					throw new PinMismatchException("Server pin could not be read. Play was not started.");
				}
				parsed.Add((name, sha, bytes));
			}
			string actualRollup = Rollup(parsed);
			string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft") ?? "";
			string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader") ?? "";
			LauncherLog.Write("pin " + source + " pin_sha256=" + pinSha + " rollup=" + actualRollup + " mc=" + minecraft + " loader=" + loader + " actualMc=" + packMc + " actualLoader=" + packLoader);
			if (!string.Equals(actualRollup, pinSha, StringComparison.OrdinalIgnoreCase))
			{
				throw new PinMismatchException(BlockCopy + " Expected pin_sha256 " + pinSha + ". Actual rollup " + actualRollup + ".");
			}
			if (!SameToken(minecraft, packMc))
			{
				throw new PinMismatchException(BlockCopy + " Expected minecraft " + minecraft + " pin_sha256 " + pinSha + ". Actual minecraft " + packMc + ".");
			}
			if (!SameToken(loader, packLoader))
			{
				throw new PinMismatchException(BlockCopy + " Expected fabric_loader " + loader + " pin_sha256 " + pinSha + ". Actual fabric_loader " + packLoader + ".");
			}
			foreach ((string name, string sha, long _) in parsed)
			{
				if (IsClientOnly(name))
				{
					continue;
				}
				string path = LocateJar(gameDir, name);
				if (!File.Exists(path))
				{
					continue;
				}
				string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
				if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
				{
					LauncherLog.Write("pin jar drift " + name + " expected=" + sha + " actual=" + actual + " pin_sha256=" + pinSha);
					throw new PinMismatchException(BlockCopy + " " + name + " does not match the server pin. Expected pin_sha256 " + pinSha + ".");
				}
			}
			AcceptedPinJson = document.RootElement.GetRawText();
		}
	}

	private static JsonDocument ParseDocument(string json)
	{
		try
		{
			return JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			throw new PinMismatchException("Server pin could not be read. Play was not started.");
		}
	}

	private static string? HostDocument()
	{
		string path = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		if (string.IsNullOrWhiteSpace(path))
		{
			path = HostPinPath;
		}
		if (!File.Exists(path))
		{
			return null;
		}
		return File.ReadAllText(path);
	}

	private static async Task<string?> TryFetchTextAsync(HttpClient http, string pinUrl, CancellationToken ct)
	{
		try
		{
			using HttpResponseMessage response = await http.GetAsync(pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			return string.IsNullOrWhiteSpace(body) ? null : body;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			return null;
		}
	}

	private static string LocateJar(string gameDir, string name)
	{
		string file = Path.GetFileName(name.Replace('\\', '/'));
		string inMods = Path.Combine(gameDir, "mods", file);
		if (File.Exists(inMods))
		{
			return inMods;
		}
		return Path.Combine(gameDir, file);
	}

	private static string Required(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			throw new PinMismatchException("Server pin could not be read. Play was not started.");
		}
		string text = value.GetString() ?? "";
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new PinMismatchException("Server pin could not be read. Play was not started.");
		}
		return text.Trim();
	}

	private static void Warn(string detail)
	{
		LastWarning = MissingCopy;
		LauncherLog.Write("pin missing " + detail + " — " + MissingCopy);
	}

	private static string? Sibling(string? url, string name)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}
		int slash = url.LastIndexOf('/');
		if (slash < 0)
		{
			return null;
		}
		return url.Substring(0, slash + 1) + name;
	}

	private static bool IsOn(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
	}

	private static string Norm(string value)
	{
		char[] buffer = new char[value.Length];
		int n = 0;
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c))
			{
				buffer[n++] = char.ToLowerInvariant(c);
			}
		}
		return new string(buffer, 0, n);
	}

	private static bool SameToken(string pin, string? pack)
	{
		if (string.IsNullOrWhiteSpace(pack))
		{
			return false;
		}
		string left = pin.Trim();
		string right = pack.Trim();
		if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return Tail(left).Equals(Tail(right), StringComparison.OrdinalIgnoreCase);
	}

	private static string Tail(string value)
	{
		int dash = value.LastIndexOf('-');
		return dash >= 0 && dash < value.Length - 1 ? value.Substring(dash + 1) : value;
	}
}
