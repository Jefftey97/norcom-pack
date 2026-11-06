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

	public const string ServerPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json";

	public const string CompatPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json";

	public const string HostFullPinPath = "G:\\MinecraftServer\\admin\\norcom-mod-pin.json";

	public const string HostSlimPinPath = "G:\\MinecraftServer\\.norcom\\server-pin.json";

	public const string AppliedPinFile = "norcom-applied-pin.txt";

	public const string BlockCopy = "Pack doesn't match the live server pin. Wait for a pack update or a pin republish, then press Play again.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public const string DefaultPackUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string LivePin = "49ff8e68f03f40d4eacdb98ad9848d17f977ef1bf2cc1f08a1e8b216c70bb9d1";

	public static string? PublishedPin { get; private set; }

	public static bool DeferAppliedWrite { get; private set; }

	public const string RequireVariable = "NORCOM_PIN_REQUIRED";

	public static readonly string[] PinUrls = new string[3] { FriendsPinUrl, ServerPinUrl, CompatPinUrl };

	public static string? LastWarning { get; private set; }

	private static string _comparedPin = "";

	private static bool _comparedDrift;

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
		Add(Sibling(packUrl, "norcom-mod-pin.json"));
		Add(Sibling(manifestUrl, "norcom-mod-pin.json"));
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

	public enum PinCheck
	{
		Open,
		Match,
		Mismatch
	}

	public static async Task<PinCheck> CheckAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl)
	{
		try
		{
			await EnforceAsync(http, gameDir, pack, ct, packUrl, manifestUrl).ConfigureAwait(false);
			return LastWarning != null ? PinCheck.Open : PinCheck.Match;
		}
		catch (PinMismatchException)
		{
			return PinCheck.Mismatch;
		}
	}

	public static Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct)
	{
		return EnforceAsync(http, gameDir, pack, ct, null, null);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl, bool missingBlocks = true)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		PublishedPin = null;
		DeferAppliedWrite = false;
		string? full = ReadIfExists(Environment.GetEnvironmentVariable("NORCOM_PIN_PATH")) ?? ReadIfExists(HostFullPinPath);
		string? slim = ReadIfExists(HostSlimPinPath);
		string source = full != null ? HostFullPinPath : "";
		if (full == null && slim == null)
		{
			foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
			{
				string? body = await PinFetch.FetchCandidateAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
				if (body == null)
				{
					continue;
				}
				if (pinUrl.EndsWith("norcom-mod-pin.json", StringComparison.OrdinalIgnoreCase))
				{
					full = body;
				}
				else
				{
					slim = body;
				}
				source = pinUrl;
				break;
			}
		}
		if (full == null && slim == null)
		{
			Warn("pin 404");
			return;
		}
		if (full != null && slim == null)
		{
			slim = await PinFetch.FetchNamedAsync(http, "server-pin.json", ct, forceTip: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		string? compat = await PinFetch.FetchNamedAsync(http, "server-compat.json", ct, forceTip: false).ConfigureAwait(continueOnCapturedContext: false);
		if (full != null)
		{
			JudgeFull(full, slim, compat, gameDir, pack, source);
			return;
		}
		JudgeSlim(slim!, compat, gameDir, pack, source);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		PublishedPin = null;
		DeferAppliedWrite = false;
		string? body = await TryFetchTextAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (body == null)
		{
			Warn("pin 404 " + pinUrl);
			return;
		}
		if (pinUrl.EndsWith("norcom-mod-pin.json", StringComparison.OrdinalIgnoreCase) || body.Contains("\"jars\"", StringComparison.Ordinal))
		{
			JudgeFull(body, null, null, gameDir, pack, pinUrl);
			return;
		}
		JudgeSlim(body, null, gameDir, pack, pinUrl);
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

	private static void JudgeFull(string json, string? slimJson, string? compatJson, string gameDir, TomlLite.Doc pack, string source)
	{
		JsonDocument document = Parse(json);
		using (document)
		{
			JsonElement root = document.RootElement;
			string minecraft = First(root, "minecraft", "mc");
			string loader = First(root, "fabric_loader", "loader");
			string pinSha = First(root, "pin_sha256", "pin");
			if (!root.TryGetProperty("jars", out var jars) || jars.ValueKind != JsonValueKind.Array)
			{
				throw new PinMismatchException("Server pin could not be read. Play was not started.");
			}
			List<(string Name, string Sha256, long Size)> parsed = ReadJars(jars);
			int dualSided = 0;
			int serverOnly = 0;
			foreach ((string name, string _, long _) in parsed)
			{
				if (IsClientOnly(name))
				{
					continue;
				}
				if (File.Exists(LocateJar(gameDir, name)))
				{
					dualSided++;
				}
				else
				{
					serverOnly++;
				}
			}
			LauncherLog.Write("pin " + source + " pin_sha256=" + pinSha + " dual-sided=" + dualSided + " server-only=" + serverOnly);
			string applied = ReadAppliedPin(gameDir) ?? "";
			if (applied.Length > 0 && !string.Equals(applied, pinSha, StringComparison.OrdinalIgnoreCase))
			{
				LauncherLog.Write("pin jar-set diff expected=" + pinSha + " actual=" + applied + " " + JarDiffSummary(gameDir, parsed));
			}
			RequirePack(minecraft, loader, pinSha, gameDir, pack, source);
			if (!string.IsNullOrWhiteSpace(slimJson))
			{
				RequireSamePin(slimJson, pinSha);
			}
			foreach ((string name, string sha, long _) in parsed)
			{
				RefuseJarDrift(gameDir, name, sha, pinSha);
			}
			if (!string.IsNullOrWhiteSpace(compatJson))
			{
				CheckCompat(compatJson, pinSha, gameDir);
			}
			if (!DeferAppliedWrite)
			{
				Remember(gameDir, pinSha);
			}
		}
	}

	private static void JudgeSlim(string json, string? compatJson, string gameDir, TomlLite.Doc pack, string source)
	{
		JsonDocument document = Parse(json);
		using (document)
		{
			JsonElement root = document.RootElement;
			string mc = First(root, "mc", "minecraft");
			string loader = First(root, "loader", "fabric_loader");
			string pin = First(root, "pin", "pin_sha256");
			RequirePack(mc, loader, pin, gameDir, pack, source);
			if (!string.IsNullOrWhiteSpace(compatJson))
			{
				CheckCompat(compatJson, pin, gameDir);
			}
			if (!DeferAppliedWrite)
			{
				Remember(gameDir, pin);
			}
		}
	}

	private static void RequirePack(string minecraft, string loader, string pin, string gameDir, TomlLite.Doc pack, string source)
	{
		string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft") ?? "";
		string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader") ?? "";
		string actualPin = ReadAppliedPin(gameDir) ?? "";
		if (!string.IsNullOrWhiteSpace(pin))
		{
			PublishedPin = pin.Trim();
		}
		LauncherLog.Write("pin " + source + " pin_sha256=" + pin + " mc=" + minecraft + " loader=" + loader + " actualPin=" + actualPin + " actualMc=" + packMc + " actualLoader=" + packLoader);
		if (!SameToken(minecraft, packMc))
		{
			throw new PinMismatchException(BlockCopy + " Expected minecraft " + minecraft + " pin_sha256 " + pin + ". Actual minecraft " + packMc + ".");
		}
		if (!SameToken(loader, packLoader))
		{
			throw new PinMismatchException(BlockCopy + " Expected fabric_loader " + loader + " pin_sha256 " + pin + ". Actual fabric_loader " + packLoader + ".");
		}
		if (!string.IsNullOrWhiteSpace(pin) && !string.Equals(pin, LivePin, StringComparison.OrdinalIgnoreCase))
		{
			LauncherLog.Write("pin warn baked=" + LivePin + " published=" + pin);
		}
		DeferAppliedWrite = actualPin.Length > 0 && !string.Equals(actualPin, pin, StringComparison.OrdinalIgnoreCase);
		if (DeferAppliedWrite)
		{
			LauncherLog.Write("pin heal applied=" + actualPin + " published=" + pin);
		}
		AcceptedPinJson = pin;
	}

	public readonly struct PinSyncResult
	{
		public string State { get; init; }

		public string Pin { get; init; }

		public string Detail { get; init; }
	}

	public static async Task<PinSyncResult> SyncPublishedAsync(HttpClient http, string gameDir, CancellationToken ct, bool forceTip)
	{
		string? json = await PinFetch.FetchNamedAsync(http, "norcom-mod-pin.json", ct, forceTip).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(json))
		{
			json = await PinFetch.FetchNamedAsync(http, "server-pin.json", ct, forceTip: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!TryPublished(json, out string pin, out List<(string Name, string Sha256, long Size)> jars, out bool hasJars))
		{
			return new PinSyncResult
			{
				State = "mismatch",
				Pin = ReadAppliedPin(gameDir) ?? "",
				Detail = "published pin could not be fetched"
			};
		}
		SoftAdopt(gameDir, pin);
		bool drift;
		if (!forceTip && string.Equals(_comparedPin, pin, StringComparison.OrdinalIgnoreCase))
		{
			drift = _comparedDrift;
		}
		else
		{
			drift = hasJars && PresentJarDiffers(gameDir, jars);
			_comparedPin = pin;
			_comparedDrift = drift;
		}
		if (drift)
		{
			return new PinSyncResult
			{
				State = "mismatch",
				Pin = pin,
				Detail = "a pack jar does not match the published pin"
			};
		}
		return new PinSyncResult
		{
			State = "correct",
			Pin = pin,
			Detail = "published pin"
		};
	}

	public static void SoftAdopt(string gameDir, string pin)
	{
		if (string.IsNullOrWhiteSpace(pin))
		{
			return;
		}
		if (!string.Equals(pin, LivePin, StringComparison.OrdinalIgnoreCase))
		{
			LauncherLog.Write("pin warn baked=" + LivePin + " published=" + pin);
		}
		PublishedPin = pin.Trim();
		Remember(gameDir, PublishedPin);
		LauncherLog.Write("pin adopted " + PublishedPin);
	}

	public static void AdoptPublished(string gameDir)
	{
		if (string.IsNullOrWhiteSpace(PublishedPin))
		{
			return;
		}
		Remember(gameDir, PublishedPin);
		LauncherLog.Write("pin adopted " + PublishedPin);
	}

	public static bool PresentJarDiffers(string gameDir, IEnumerable<(string Name, string Sha256, long Size)> jars)
	{
		foreach ((string name, string sha, long _) in jars)
		{
			if (Drifted(gameDir, name, sha, out _))
			{
				return true;
			}
		}
		return false;
	}

	public static string JarDiffSummary(string gameDir, IEnumerable<(string Name, string Sha256, long Size)> expected)
	{
		List<string> lines = new List<string>();
		foreach ((string name, string sha, long _) in expected)
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
				lines.Add(Path.GetFileName(name) + " expected=" + sha + " actual=" + actual);
			}
		}
		if (lines.Count == 0)
		{
			return "jar-set matches";
		}
		return string.Join("; ", lines);
	}

	private static void RequireSamePin(string slimJson, string pinSha)
	{
		JsonDocument slim = Parse(slimJson);
		using (slim)
		{
			string pin = First(slim.RootElement, "pin", "pin_sha256");
			if (!string.Equals(pin, pinSha, StringComparison.OrdinalIgnoreCase))
			{
				LauncherLog.Write("pin slim mismatch pin=" + pin + " pin_sha256=" + pinSha);
				throw new PinMismatchException(BlockCopy + " Expected pin " + pinSha + ". Slim pin " + pin + ".");
			}
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
				if (compatPin.Length > 0 && !string.Equals(compatPin, expectedPin, StringComparison.OrdinalIgnoreCase))
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
				if (!TryJar(entry.Name, entry.Value, out var jar, out var hash))
				{
					continue;
				}
				RefuseJarDrift(gameDir, jar, hash, expectedPin);
			}
		}
	}

	private static void RefuseJarDrift(string gameDir, string name, string sha, string pin)
	{
		if (!Drifted(gameDir, name, sha, out string actual))
		{
			return;
		}
		LauncherLog.Write("pin jar drift " + name + " expected=" + sha + " actual=" + actual + " pin_sha256=" + pin);
		throw new PinMismatchException(BlockCopy + " " + Path.GetFileName(name) + " does not match the server pin. Expected pin_sha256 " + pin + ".");
	}

	private static bool Drifted(string gameDir, string name, string sha, out string actual)
	{
		actual = "";
		if (IsClientOnly(name))
		{
			return false;
		}
		string path = LocateJar(gameDir, name);
		if (!File.Exists(path))
		{
			return false;
		}
		actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
		return !string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryPublished(string json, out string pin, out List<(string Name, string Sha256, long Size)> jars, out bool hasJars)
	{
		pin = "";
		jars = null;
		hasJars = false;
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			pin = Optional(root, "pin_sha256") ?? Optional(root, "pin") ?? "";
			if (pin.Length == 0)
			{
				return false;
			}
			if (root.TryGetProperty("jars", out JsonElement jarsElement) && jarsElement.ValueKind == JsonValueKind.Array)
			{
				hasJars = true;
				jars = ReadJars(jarsElement);
			}
			return true;
		}
		catch (Exception ex) when (ex is JsonException or PinMismatchException)
		{
			return false;
		}
	}

	private static string Optional(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		string text = (value.GetString() ?? "").Trim();
		return text.Length == 0 ? null : text;
	}

	private static List<(string Name, string Sha256, long Size)> ReadJars(JsonElement jars)
	{
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
		return parsed;
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

	private static string First(JsonElement element, string primary, string alias)
	{
		string? Read(string name)
		{
			if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return null;
			}
			string text = value.GetString() ?? "";
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}
		return Read(primary) ?? Read(alias) ?? throw new PinMismatchException("Server pin could not be read. Play was not started.");
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

	private static string? ReadIfExists(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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
