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

	public const string HostPinPath = "G:\\MinecraftServer\\.norcom\\server-pin.json";

	public static readonly string[] PinUrls = new string[3] { "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/norcom-mod-pin.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json" };

	public const string BlockCopy = "Pack doesn't match live server pin — wait for update";

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
		Add(Sibling(packUrl, "pin.json"));
		Add(Sibling(manifestUrl, "pin.json"));
		string[] pinUrls = PinUrls;
		foreach (string url in pinUrls)
		{
			Add(url);
		}
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
			if (MissingRefuses)
			{
				LauncherLog.Write("pin pack.toml " + ex.GetType().Name);
				throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
			}
			Warn("pack.toml " + ex.GetType().Name);
			return;
		}
		await EnforceAsync(http, gameDir, TomlLite.Parse(body), ct, packUrl, manifestUrl).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
		{
			HttpResponseMessage response;
			try
			{
				response = await http.GetAsync(pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				continue;
			}
			if (response.StatusCode == System.Net.HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
			{
				response.Dispose();
				continue;
			}
			await ReadPinAsync(response, gameDir, pack, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		if (MissingRefuses)
		{
			LauncherLog.Write("pin missing required");
			throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
		}
		Warn("pin 404");
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
			if (MissingRefuses)
			{
				LauncherLog.Write("pin fetch failed required " + ex.GetType().Name);
				throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
			}
			Warn("pin fetch failed " + ex.GetType().Name);
			return;
		}
		int status = (int)response.StatusCode;
		if (response.StatusCode == System.Net.HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
		{
			response.Dispose();
			if (MissingRefuses)
			{
				LauncherLog.Write("pin " + status + " required");
				throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
			}
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
				if (MissingRefuses)
				{
					LauncherLog.Write("pin empty required " + pinUrl);
					throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
				}
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
				string reason = await DescribeMismatchAsync(document.RootElement, pack, gameDir, ct).ConfigureAwait(continueOnCapturedContext: false);
				string line = "pin " + pinUrl + " " + Summarize(document.RootElement) + (reason == null ? " ok" : " block " + reason);
				LauncherLog.Write(line);
				if (reason != null)
				{
					throw new PinMismatchException("Pack doesn't match live server pin — wait for update");
				}
				AcceptedPinJson = body;
				TryRemember(gameDir, body);
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

	public static async Task<string?> DescribeMismatchAsync(JsonElement pin, TomlLite.Doc pack, string gameDir, CancellationToken ct)
	{
		if (pin.ValueKind != JsonValueKind.Object)
		{
			return "not-object";
		}
		string? Field(string name)
		{
			if (!pin.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return null;
			}
			string text = value.GetString();
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}
		string? mc = Field("mc");
		string? loader = Field("loader");
		string? packVersion = Field("packVersion");
		string? indexSha = Field("indexSha256");
		string? pinId = Field("pin");
		string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft");
		string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader");
		string packVersionActual = pack.Keys.GetValueOrDefault("version");
		string packIndex = pack.Keys.GetValueOrDefault("index.hash") ?? "";
		if (mc != null && !SameToken(mc, packMc))
		{
			return "mc";
		}
		if (loader != null && !SameToken(loader, packLoader))
		{
			return "loader";
		}
		if (packVersion != null && !string.Equals(packVersion, packVersionActual?.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			return "packVersion";
		}
		if (indexSha != null && !Hashing.EqualsHex(packIndex, indexSha))
		{
			return "indexSha256";
		}
		if (pinId != null && !Hashing.EqualsHex(packIndex, pinId))
		{
			return "pin";
		}
		if (!pin.TryGetProperty("both", out var both) || both.ValueKind != JsonValueKind.Object)
		{
			return null;
		}
		foreach (JsonProperty entry in both.EnumerateObject())
		{
			if (!TryJar(entry.Value, out var jar, out var hash))
			{
				continue;
			}
			if (IsClientOnly(entry.Name, jar))
			{
				continue;
			}
			string path = Path.Combine(gameDir, jar.Replace('/', Path.DirectorySeparatorChar));
			if (!File.Exists(path))
			{
				return "both-missing " + entry.Name;
			}
			string actual = await Hashing.FileHexAsync(path, "sha256", ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!Hashing.EqualsHex(actual, hash))
			{
				return "both " + entry.Name;
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

	private static bool TryJar(JsonElement value, out string jar, out string hash)
	{
		jar = "";
		hash = "";
		if (value.ValueKind == JsonValueKind.Object)
		{
			jar = value.TryGetProperty("jar", out var jarValue) ? jarValue.GetString() ?? "" : "";
			hash = value.TryGetProperty("hash", out var hashValue) ? hashValue.GetString() ?? "" : "";
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
		}
		return !string.IsNullOrWhiteSpace(jar) && !string.IsNullOrWhiteSpace(hash);
	}

	public static bool IsClientOnly(string modid, string jar)
	{
		string path = (jar ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
		if (path.StartsWith("shaderpacks/", StringComparison.Ordinal) || path.StartsWith("resourcepacks/", StringComparison.Ordinal) || path.Contains("/shaderpacks/", StringComparison.Ordinal) || path.Contains("/resourcepacks/", StringComparison.Ordinal))
		{
			return true;
		}
		string id = Norm(modid);
		return id is "sodium" or "iris" or "inventoryprofilesnext" or "ipn" or "rei" or "roughlyenoughitems" or "distanthorizons" or "dh";
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
