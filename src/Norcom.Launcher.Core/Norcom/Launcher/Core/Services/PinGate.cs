using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
	public const string ServerPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json";

	public const string CompatPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json";

	public const string HostPinPath = "G:\\MinecraftServer\\.norcom\\server-pin.json";

	public const string AppliedPinFile = "norcom-applied-pin.txt";

	public const string BlockCopy = "Pack doesn't match live server pin — wait for update. Repair before you play.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public const string DefaultPackUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string RequireVariable = "NORCOM_PIN_REQUIRED";

	public static readonly string[] PinUrls = new string[2] { ServerPinUrl, CompatPinUrl };

	public static string? LastWarning { get; private set; }

	public static string? AcceptedPinJson { get; private set; }

	public static bool MissingRefuses => IsOn(Environment.GetEnvironmentVariable("NORCOM_PIN_REQUIRED"));

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
		foreach (string url in PinUrls)
		{
			Add(url);
		}
		Add(Sibling(packUrl, "server-pin.json"));
		Add(Sibling(manifestUrl, "server-pin.json"));
		return urls;
	}

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
		string? host = ReadHostPin();
		string? slim = host;
		string slimSource = HostPinPath;
		if (slim == null)
		{
			foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
			{
				slim = await TryFetchTextAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (slim == null)
				{
					continue;
				}
				slimSource = pinUrl;
				break;
			}
		}
		if (slim == null)
		{
			if (MissingRefuses)
			{
				LauncherLog.Write("pin missing required");
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pin 404");
			return;
		}
		string? compat = null;
		if (!slimSource.EndsWith("server-compat.json", StringComparison.OrdinalIgnoreCase))
		{
			compat = await TryFetchTextAsync(http, CompatPinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		Judge(slim, compat, gameDir, pack, slimSource);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		string? slim = await TryFetchTextAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (slim == null)
		{
			if (MissingRefuses)
			{
				LauncherLog.Write("pin missing required " + pinUrl);
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pin 404 " + pinUrl);
			return;
		}
		Judge(slim, null, gameDir, pack, pinUrl);
	}

	public static string? ReadAppliedPin(string gameDir)
	{
		string path = Path.Combine(gameDir, AppliedPinFile);
		if (!File.Exists(path))
		{
			return null;
		}
		string text = File.ReadAllText(path).Trim();
		return string.IsNullOrWhiteSpace(text) ? null : text;
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

	private static void Judge(string slimJson, string? compatJson, string gameDir, TomlLite.Doc pack, string source)
	{
		JsonDocument slim = Parse(slimJson);
		using (slim)
		{
			JsonElement root = slim.RootElement;
			string mc = Required(root, "mc");
			string loader = Required(root, "loader");
			string expectedPin = Required(root, "pin");
			string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft") ?? "";
			string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader") ?? "";
			string actualPin = ReadAppliedPin(gameDir) ?? "";
			LauncherLog.Write("pin " + source + " pin=" + expectedPin + " mc=" + mc + " loader=" + loader + " actualPin=" + actualPin + " actualMc=" + packMc + " actualLoader=" + packLoader);
			if (!SameToken(mc, packMc))
			{
				throw new PinMismatchException(BlockCopy + " Expected mc " + mc + " pin " + expectedPin + ". Actual mc " + packMc + ".");
			}
			if (!SameToken(loader, packLoader))
			{
				throw new PinMismatchException(BlockCopy + " Expected loader " + loader + " pin " + expectedPin + ". Actual loader " + packLoader + ".");
			}
			if (actualPin.Length > 0 && !string.Equals(actualPin, expectedPin, StringComparison.Ordinal))
			{
				throw new PinMismatchException(BlockCopy + " Expected pin " + expectedPin + ". Actual pin " + actualPin + ".");
			}
			if (!string.IsNullOrWhiteSpace(compatJson))
			{
				CheckCompat(compatJson, expectedPin, gameDir);
			}
			Remember(gameDir, expectedPin);
			AcceptedPinJson = "{\"schema\":1,\"mc\":" + JsonSerializer.Serialize(mc) + ",\"loader\":" + JsonSerializer.Serialize(loader) + ",\"pin\":" + JsonSerializer.Serialize(expectedPin) + ",\"source\":\"server\"}";
		}
	}

	private static void CheckCompat(string json, string expectedPin, string gameDir)
	{
		JsonDocument compat = Parse(json);
		using (compat)
		{
			JsonElement root = compat.RootElement;
			if (root.TryGetProperty("pin", out var pinValue) && pinValue.ValueKind == JsonValueKind.String)
			{
				string compatPin = (pinValue.GetString() ?? "").Trim();
				if (compatPin.Length > 0 && !string.Equals(compatPin, expectedPin, StringComparison.Ordinal))
				{
					LauncherLog.Write("pin compat mismatch expected=" + expectedPin + " compat=" + compatPin);
					throw new PinMismatchException(BlockCopy + " Expected pin " + expectedPin + ". Compat pin " + compatPin + ".");
				}
			}
			if (!root.TryGetProperty("both", out var both) || both.ValueKind != JsonValueKind.Object)
			{
				return;
			}
			foreach (JsonProperty entry in both.EnumerateObject())
			{
				if (!TryJar(entry.Name, entry.Value, out var jar, out var hash) || IsClientOnly(entry.Name) || IsClientOnly(jar))
				{
					continue;
				}
				string path = LocateJar(gameDir, jar);
				if (!File.Exists(path))
				{
					continue;
				}
				string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
				if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
				{
					LauncherLog.Write("pin jar drift " + jar + " expected=" + hash + " actual=" + actual + " pin=" + expectedPin);
					throw new PinMismatchException(BlockCopy + " " + Path.GetFileName(jar) + " does not match the server pin. Expected pin " + expectedPin + ".");
				}
			}
		}
	}

	private static bool TryJar(string name, JsonElement value, out string jar, out string hash)
	{
		jar = "";
		hash = "";
		if (value.ValueKind == JsonValueKind.Object)
		{
			jar = value.TryGetProperty("jar", out var jarValue) ? jarValue.GetString() ?? "" : name;
			hash = value.TryGetProperty("hash", out var hashValue) ? hashValue.GetString() ?? "" : "";
			if (string.IsNullOrWhiteSpace(hash) && value.TryGetProperty("sha256", out var sha))
			{
				hash = sha.GetString() ?? "";
			}
		}
		else if (value.ValueKind == JsonValueKind.String)
		{
			string text = value.GetString() ?? "";
			int split = text.IndexOf('|');
			if (split > 0)
			{
				jar = text.Substring(0, split).Trim();
				hash = text.Substring(split + 1).Trim();
			}
			else
			{
				jar = name;
				hash = text.Trim();
			}
		}
		return !string.IsNullOrWhiteSpace(jar) && !string.IsNullOrWhiteSpace(hash);
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

	private static string? ReadHostPin()
	{
		string path = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		if (string.IsNullOrWhiteSpace(path))
		{
			path = HostPinPath;
		}
		return File.Exists(path) ? File.ReadAllText(path) : null;
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

	private static void Remember(string gameDir, string pin)
	{
		try
		{
			Directory.CreateDirectory(gameDir);
			File.WriteAllText(Path.Combine(gameDir, AppliedPinFile), pin.Trim() + Environment.NewLine);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pin remember " + ex.GetType().Name);
		}
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

	private static JsonDocument Parse(string json)
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
