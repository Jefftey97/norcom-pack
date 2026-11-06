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

public sealed class PinMismatchException : InvalidOperationException
{
	public PinMismatchException(string message)
		: base(message)
	{
	}
}

public static class PinGate
{
	public const string DefaultPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/norcom-mod-pin.json";

	public const string CompatPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json";

	public const string HostPinPath = "G:\\MinecraftServer\\.norcom\\server-pin.json";

	public static readonly string[] PinUrls = new string[3] { "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/norcom-mod-pin.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json" };

	public const string BlockCopy = "Pack doesn't match live server pin — wait for update. Repair before you play.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public const string DefaultPackUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string RequireVariable = "NORCOM_PIN_REQUIRED";

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
		string[] pinUrls = PinUrls;
		foreach (string url in pinUrls)
		{
			Add(url);
		}
		Add(Sibling(packUrl, "pin.json"));
		Add(Sibling(manifestUrl, "pin.json"));
		return urls;
	}

	public static async Task EnforceFeedAsync(HttpClient http, string gameDir, CancellationToken ct)
	{
		string text = Environment.GetEnvironmentVariable("NORCOM_PACK_URL");
		string packUrl = (string.IsNullOrWhiteSpace(text) ? "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml" : text.Trim());
		string manifestUrl = Environment.GetEnvironmentVariable("NORCOM_UPDATE_MANIFEST");
		string body;
		try
		{
			body = await http.GetStringAsync(packUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			Warn("pack.toml " + ex.GetType().Name);
			return;
		}
		await EnforceAsync(http, gameDir, TomlLite.Parse(body), ct, packUrl, manifestUrl).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl, bool missingBlocks = true)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		JsonDocument? identity = null;
		JsonDocument? bothSource = null;
		string identityUrl = "";
		try
		{
			foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
			{
				JsonDocument? parsed = await TryFetchPinAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (parsed == null)
				{
					continue;
				}
				if (identity == null)
				{
					identity = parsed;
					identityUrl = pinUrl;
				}
				else if (bothSource == null && HasBoth(parsed.RootElement))
				{
					bothSource = parsed;
				}
				else
				{
					parsed.Dispose();
				}
				if (identity != null && (HasBoth(identity.RootElement) || bothSource != null))
				{
					break;
				}
			}
			if (identity == null)
			{
				Warn("pin 404");
				return;
			}
			if (bothSource == null && !HasBoth(identity.RootElement))
			{
				bothSource = await TryFetchPinAsync(http, "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json", ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			JsonElement both = HasBoth(identity.RootElement) ? identity.RootElement : (bothSource != null && HasBoth(bothSource.RootElement) ? bothSource.RootElement : identity.RootElement);
			await JudgeAsync(identity.RootElement, both, gameDir, pack, identityUrl, missingBlocks, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			identity?.Dispose();
			bothSource?.Dispose();
		}
	}

	public static Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct)
	{
		return EnforceAsync(http, gameDir, pack, ct, null, null);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		HttpResponseMessage response;
		try
		{
			response = await http.GetAsync(pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			Warn("pin fetch failed " + ex.GetType().Name);
			return;
		}
		int status = (int)response.StatusCode;
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
		{
			response.Dispose();
			Warn("pin " + status);
			return;
		}
		await ReadPinAsync(response, gameDir, pack, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task ReadPinAsync(HttpResponseMessage response, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		using (response)
		{
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(body))
			{
				Warn("pin empty " + pinUrl);
				return;
			}
			JsonDocument document;
			try
			{
				document = JsonDocument.Parse(body);
			}
			catch (JsonException)
			{
				throw new PinMismatchException("Server pin could not be read. Play was not started.");
			}
			using (document)
			{
				await JudgeAsync(document.RootElement, document.RootElement, gameDir, pack, pinUrl, missingBlocks: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	public static void TryRemember(string gameDir, string json)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(gameDir) || string.IsNullOrWhiteSpace(json))
			{
				return;
			}
			Directory.CreateDirectory(gameDir);
			File.WriteAllText(Path.Combine(gameDir, "norcom-applied-pin.json"), json.Trim() + Environment.NewLine);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pin remember " + ex.GetType().Name);
		}
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

	public static Task<string?> DescribeMismatchAsync(JsonElement pin, TomlLite.Doc pack, string gameDir, CancellationToken ct)
	{
		return DescribeMismatchAsync(pin, pin, pack, gameDir, ct, missingBlocks: true);
	}

	public static async Task<string?> DescribeMismatchAsync(JsonElement pin, JsonElement bothDoc, TomlLite.Doc pack, string gameDir, CancellationToken ct, bool missingBlocks)
	{
		if (pin.ValueKind != JsonValueKind.Object)
		{
			return "not-object";
		}
		string? mc = FirstString(pin, "mc", "minecraft");
		string? loader = FirstString(pin, "loader", "fabric_loader");
		string? packVersion = FirstString(pin, "packVersion");
		string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft");
		string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader");
		string packVersionActual = pack.Keys.GetValueOrDefault("version");
		if (mc != null && !SameToken(mc, packMc))
		{
			return "mc expected " + mc + " actual " + (packMc ?? "");
		}
		if (loader != null && !SameToken(loader, packLoader))
		{
			return "loader expected " + loader + " actual " + (packLoader ?? "");
		}
		if (packVersion != null && !string.Equals(packVersion, packVersionActual?.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return "packVersion expected " + packVersion + " actual " + (packVersionActual ?? "");
		}
		if (!bothDoc.TryGetProperty("both", out var both) || both.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		foreach (JsonProperty entry in both.EnumerateObject())
		{
			if (!TryJar(entry.Name, entry.Value, out var jar, out var hash))
			{
				continue;
			}
			if (IsClientOnly(entry.Name, jar))
			{
				continue;
			}
			string path = LocateJar(gameDir, jar);
			if (!File.Exists(path))
			{
				if (missingBlocks)
				{
					return "both-missing " + Path.GetFileName(jar) + " expected " + hash;
				}
				continue;
			}
			string actual = await Hashing.FileHexAsync(path, "sha256", ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!Hashing.EqualsHex(actual, hash))
			{
				return "both " + Path.GetFileName(jar) + " expected " + hash + " actual " + actual;
			}
		}
		return null;
	}

	private static void Warn(string detail)
	{
		LastWarning = MissingCopy;
		LauncherLog.Write("pin missing " + detail + " — " + MissingCopy);
	}

	private static string Summarize(JsonElement pin)
	{
		string Pick(string name)
		{
			return pin.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
		}
		return "schema=" + (pin.TryGetProperty("schema", out var schema) ? schema.ToString() : "") + " mc=" + Pick("mc") + " loader=" + Pick("loader") + " packVersion=" + Pick("packVersion") + " indexSha256=" + Pick("indexSha256") + " source=" + Pick("source") + " updatedUtc=" + Pick("updatedUtc");
	}

	private static bool TryJar(string name, JsonElement value, out string jar, out string hash)
	{
		jar = "";
		hash = "";
		if (value.ValueKind == JsonValueKind.Object)
		{
			jar = value.TryGetProperty("jar", out var jarValue) ? jarValue.GetString() ?? "" : "";
			if (string.IsNullOrWhiteSpace(jar))
			{
				jar = name;
			}
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

	private static string LocateJar(string gameDir, string jar)
	{
		string relative = jar.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
		string direct = Path.Combine(gameDir, relative);
		if (File.Exists(direct))
		{
			return direct;
		}
		string named = Path.Combine(gameDir, "mods", Path.GetFileName(relative));
		if (File.Exists(named))
		{
			return named;
		}
		return direct;
	}

	private static async Task<JsonDocument?> TryFetchPinAsync(HttpClient http, string pinUrl, CancellationToken ct)
	{
		try
		{
			using HttpResponseMessage response = await http.GetAsync(pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(body))
			{
				return null;
			}
			return JsonDocument.Parse(body);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
			return null;
		}
	}

	private static bool HasBoth(JsonElement pin)
	{
		return pin.ValueKind == JsonValueKind.Object && pin.TryGetProperty("both", out var both) && both.ValueKind == JsonValueKind.Object;
	}

	private static async Task JudgeAsync(JsonElement pin, JsonElement both, string gameDir, TomlLite.Doc pack, string pinUrl, bool missingBlocks, CancellationToken ct)
	{
		string expected = FirstString(pin, "pin", "pin_sha256") ?? "";
		string reason = await DescribeMismatchAsync(pin, both, pack, gameDir, ct, missingBlocks).ConfigureAwait(continueOnCapturedContext: false);
		string line = "pin " + pinUrl + " expected=" + expected + " mc=" + (FirstString(pin, "mc", "minecraft") ?? "") + " loader=" + (FirstString(pin, "loader", "fabric_loader") ?? "") + (reason == null ? " ok" : " block " + reason);
		LauncherLog.Write(line);
		if (reason != null)
		{
			throw new PinMismatchException(BlockCopy + " Expected pin " + expected + ". " + reason);
		}
		AcceptedPinJson = pin.GetRawText();
		TryRemember(gameDir, AcceptedPinJson);
	}

	private static string? FirstString(JsonElement pin, string primary, string? alias = null)
	{
		string? Read(string name)
		{
			if (!pin.TryGetProperty(name, out var value))
			{
				return null;
			}
			if (value.ValueKind == JsonValueKind.String)
			{
				string text = value.GetString();
				return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
			}
			if (value.ValueKind == JsonValueKind.Number)
			{
				return value.ToString();
			}
			return null;
		}
		return Read(primary) ?? (alias == null ? null : Read(alias));
	}

	public static bool IsClientOnly(string modid, string jar)
	{
		string path = (jar ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
		if (path.StartsWith("shaderpacks/", StringComparison.Ordinal) || path.StartsWith("resourcepacks/", StringComparison.Ordinal) || path.Contains("/shaderpacks/", StringComparison.Ordinal) || path.Contains("/resourcepacks/", StringComparison.Ordinal))
		{
			return true;
		}
		string id = Norm(modid + " " + Path.GetFileName(jar ?? ""));
		return id.Contains("sodium") || id.Contains("iris") || id.Contains("inventoryprofiles") || id.Contains("roughlyenoughitems") || id.Contains("distanthorizons") || id is "ipn" or "rei" or "dh" || id.StartsWith("ipn") || id.StartsWith("rei") || id.StartsWith("dh");
	}

	private static string Norm(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
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
