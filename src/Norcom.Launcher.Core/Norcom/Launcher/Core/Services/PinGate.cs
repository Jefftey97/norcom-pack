using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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

	public const string AppliedPinFile = "norcom-applied-pin.txt";

	public const string PrimaryAppliedPinFile = "applied-pin.json";

	public const string MarbleJarName = "norcom-marble-1.0.0.jar";

	public const string BlockCopy = "Pack doesn't match the live server pin. Wait for a pack update or a pin republish, then press Play again.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public const string DefaultPackUrl = "https://github.com/Jefftey97/norcom-pack/raw/refs/heads/main/pack.toml";

	public const string LivePin = "0517b6b64a967471914d12f7567823ac8a329e7d7e2200575b841350fc3afc48";

	public const string LivePinGeneratedAt = "2026-11-06T13:04:44-07:00";

	public const string PreviousPin = "49ff8e68f03f40d4eacdb98ad9848d17f977ef1bf2cc1f08a1e8b216c70bb9d1";

	public const string LiveMarbleSha = "3566420dd6cfc2a104c4b7b17bfeebedb20551d2f32b1502fe03748ca9728c48";

	public static string? PublishedPin { get; private set; }

	public static bool DeferAppliedWrite { get; private set; }

	public const string RequireVariable = "NORCOM_PIN_REQUIRED";

	public static readonly string[] PinUrls = new string[3] { FriendsPinUrl, ServerPinUrl, CompatPinUrl };

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
		// Friends use the published channel. An unrelated owner installation on
		// the same PC must not silently replace it with a stale private pin.
		string? overridePath = Environment.GetEnvironmentVariable("NORCOM_PIN_PATH");
		string? full = ReadIfExists(overridePath);
		string? slim = null;
		string source = full != null ? overridePath! : "";
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

	public static Task<PinSyncResult> SyncPublishedAsync(HttpClient http, string gameDir, CancellationToken ct, bool forceTip)
	{
		return SyncPublishedAsync(http, gameDir, ct, forceTip, null);
	}

	public static async Task<PinSyncResult> SyncPublishedAsync(HttpClient http, string gameDir, CancellationToken ct, bool forceTip, string? launcherDir)
	{
		string primaryDir = string.IsNullOrWhiteSpace(launcherDir) ? NorcomPaths.LauncherDir : launcherDir.Trim();
		string? json = await PinFetch.FetchNamedAsync(http, "norcom-mod-pin.json", ct, forceTip, primaryDir).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrWhiteSpace(json))
		{
			json = await PinFetch.FetchNamedAsync(http, "server-pin.json", ct, forceTip: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!TryPublished(json, out string pin, out List<(string Name, string Sha256, long Size, string Url)> jars, out bool hasJars))
		{
			return Mismatch(gameDir, "published pin could not be fetched");
		}
		if (IsTemporaryPin(pin))
		{
			LauncherLog.Write("pin refuse TEMP " + pin.Trim());
			return Mismatch(gameDir, "published pin is temporary");
		}
		if (IsDowngradeDocument(json))
		{
			LauncherLog.Write("pin refuse downgrade baked=" + LivePin + " published=" + pin.Trim());
			return Mismatch(gameDir, "published pin is older than the baked pin");
		}
		if (IsOlderThanApplied(json, primaryDir))
		{
			// A cached or archival copy must never move friends back to an older
			// pin than the one this PC already confirmed. Jars and stamps stay.
			LauncherLog.Write("pin refuse older-than-applied published=" + pin.Trim());
			return Mismatch(gameDir, "published pin is older than the applied pin");
		}
		if (!hasJars)
		{
			return Mismatch(gameDir, "published pin has no jar list");
		}
		List<(string Name, string Sha256, long Size, string Url)> lagging = LaggingJars(gameDir, jars);
		if (lagging.Count > 0)
		{
			LauncherLog.Write("pin heal jars=" + lagging.Count + " published=" + pin);
			if (!await TryHealJarsAsync(http, gameDir, lagging, ct).ConfigureAwait(continueOnCapturedContext: false))
			{
				LauncherLog.Write("pin heal failed published=" + pin);
				return Mismatch(gameDir, "a pack jar does not match the published pin");
			}
			if (LaggingJars(gameDir, jars).Count > 0)
			{
				LauncherLog.Write("pin heal failed published=" + pin);
				return Mismatch(gameDir, "a pack jar does not match the published pin");
			}
		}
		if (!CommitApplied(gameDir, primaryDir, pin, PublishedAtText(json)))
		{
			return Mismatch(gameDir, "applied pin could not be saved");
		}
		if (!string.Equals(pin, LivePin, StringComparison.OrdinalIgnoreCase))
		{
			LauncherLog.Write("pin warn baked=" + LivePin + " published=" + pin);
		}
		return new PinSyncResult
		{
			State = "correct",
			Pin = pin,
			Detail = "published pin"
		};
	}

	public static bool IsDowngradeDocument(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			string pin = Optional(root, "pin_sha256") ?? Optional(root, "pin") ?? "";
			string generatedAt = Optional(root, "generated_at") ?? "";
			return IsDowngrade(pin, generatedAt);
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static bool IsDowngrade(string? pin, string? generatedAt)
	{
		string text = (pin ?? "").Trim();
		if (text.Length == 0)
		{
			return false;
		}
		if (string.Equals(text, LivePin, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (string.Equals(text, PreviousPin, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (DateTimeOffset.TryParse(generatedAt, out DateTimeOffset published) && DateTimeOffset.TryParse(LivePinGeneratedAt, out DateTimeOffset baked))
		{
			return published <= baked;
		}
		return false;
	}

	public static bool IsTemporaryPin(string? pin)
	{
		string text = (pin ?? "").Trim();
		return text.Equals("TEMP", StringComparison.OrdinalIgnoreCase) || text.StartsWith("TEMP-", StringComparison.OrdinalIgnoreCase) || text.StartsWith("TEMP_", StringComparison.OrdinalIgnoreCase);
	}

	public static string? ReadPrimaryAppliedGeneratedAt(string launcherDir)
	{
		string path = Path.Combine(launcherDir, PrimaryAppliedPinFile);
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			return Optional(document.RootElement, "generated_at");
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	// True when a fetched pin document is older than the pin this PC already
	// applied. Pins only move forward unless the owner republishes with a newer
	// generated_at; cached, CDN or archival copies cannot roll friends back.
	public static bool IsOlderThanApplied(string? json, string? launcherDir)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}
		string dir = string.IsNullOrWhiteSpace(launcherDir) ? NorcomPaths.LauncherDir : launcherDir.Trim();
		string? applied = ReadPrimaryAppliedPin(dir)?.Trim();
		if (string.IsNullOrEmpty(applied))
		{
			return false;
		}
		string pin;
		string generatedAt;
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}
			pin = Optional(root, "pin_sha256") ?? Optional(root, "pin") ?? "";
			generatedAt = Optional(root, "generated_at") ?? "";
		}
		catch (JsonException)
		{
			return false;
		}
		if (pin.Length == 0 || string.Equals(pin, applied, StringComparison.OrdinalIgnoreCase) || !TryTime(generatedAt, out DateTimeOffset published))
		{
			return false;
		}
		if (TryTime(ReadPrimaryAppliedGeneratedAt(dir), out DateTimeOffset appliedAt))
		{
			return published < appliedAt;
		}
		// Older stamps carry no publication time. Any applied pin other than the
		// baked/previous/temporary ones was accepted as newer than the baked pin,
		// so a document published no later than the baked pin is stale.
		if (string.Equals(applied, LivePin, StringComparison.OrdinalIgnoreCase) || string.Equals(applied, PreviousPin, StringComparison.OrdinalIgnoreCase) || IsTemporaryPin(applied))
		{
			return false;
		}
		return TryTime(LivePinGeneratedAt, out DateTimeOffset baked) && published <= baked;
	}

	private static string PublishedAtText(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return "";
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			string text = document.RootElement.ValueKind == JsonValueKind.Object ? Optional(document.RootElement, "generated_at") ?? "" : "";
			return TryTime(text, out _) ? text : "";
		}
		catch (JsonException)
		{
			return "";
		}
	}

	private static bool TryTime(string? text, out DateTimeOffset value)
	{
		return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
	}

	public static string? ReadPrimaryAppliedPin(string launcherDir)
	{
		string path = Path.Combine(launcherDir, PrimaryAppliedPinFile);
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;
			string pin = Optional(root, "pin_sha256") ?? Optional(root, "pin") ?? "";
			return pin.Length == 0 ? null : pin;
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
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
		LauncherLog.Write("pin adopted " + PublishedPin + " game=" + gameDir);
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

	private static PinSyncResult Mismatch(string gameDir, string detail)
	{
		return new PinSyncResult
		{
			State = "mismatch",
			Pin = ReadAppliedPin(gameDir) ?? "",
			Detail = detail
		};
	}

	private static List<(string Name, string Sha256, long Size, string Url)> LaggingJars(string gameDir, IEnumerable<(string Name, string Sha256, long Size, string Url)> jars)
	{
		List<(string Name, string Sha256, long Size, string Url)> lagging = new List<(string, string, long, string)>();
		foreach ((string name, string sha, long size, string url) in jars)
		{
			if (IsClientOnly(name))
			{
				continue;
			}
			string path = LocateJar(gameDir, name);
			if (!File.Exists(path))
			{
				if (IsMarbleJar(name))
				{
					lagging.Add((name, sha, size, url));
				}
				continue;
			}
			if (Drifted(gameDir, name, sha, out _))
			{
				lagging.Add((name, sha, size, url));
			}
		}
		return lagging;
	}

	private static bool IsMarbleJar(string name)
	{
		return Path.GetFileName((name ?? "").Replace('\\', '/')).Equals(MarbleJarName, StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<bool> TryHealJarsAsync(HttpClient http, string gameDir, IReadOnlyList<(string Name, string Sha256, long Size, string Url)> jars, CancellationToken ct)
	{
		List<(string Dest, string Temp)> staged = new List<(string, string)>();
		List<(string Dest, string? Backup)> placed = new List<(string, string?)>();
		bool committed = false;
		try
		{
			foreach ((string name, string sha, long size, string url) in jars)
			{
				ct.ThrowIfCancellationRequested();
				string temp = Path.Combine(Path.GetTempPath(), "norcom-jar-" + Guid.NewGuid().ToString("n") + ".part");
				if (!await TryDownloadVerifiedAsync(http, name, sha, size, url, temp, ct).ConfigureAwait(continueOnCapturedContext: false))
				{
					return false;
				}
				staged.Add((HealDest(gameDir, name), temp));
			}
			// Replace every jar or none: each previous jar is kept beside its
			// destination until all replacements succeed, then restored on failure.
			foreach ((string dest, string temp) in staged)
			{
				string directory = Path.GetDirectoryName(dest);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}
				string? backup = null;
				if (File.Exists(dest))
				{
					backup = dest + ".norcom-heal-" + Guid.NewGuid().ToString("n") + ".bak";
					File.Move(dest, backup);
				}
				placed.Add((dest, backup));
				File.Move(temp, dest);
			}
			committed = true;
			foreach ((string dest, string? backup) in placed)
			{
				LauncherLog.Write("pin heal jar " + Path.GetFileName(dest));
				if (backup != null)
				{
					TryDeleteQuietly(backup);
				}
			}
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
		{
			LauncherLog.Write("pin heal failed " + ex.GetType().Name);
			return false;
		}
		finally
		{
			if (!committed)
			{
				RestoreHealedJars(placed);
			}
			foreach ((string _, string temp) in staged)
			{
				try
				{
					if (File.Exists(temp))
					{
						File.Delete(temp);
					}
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
				}
			}
		}
	}

	private static void RestoreHealedJars(List<(string Dest, string? Backup)> placed)
	{
		for (int i = placed.Count - 1; i >= 0; i--)
		{
			(string dest, string? backup) = placed[i];
			try
			{
				if (backup == null)
				{
					if (File.Exists(dest))
					{
						File.Delete(dest);
					}
				}
				else if (File.Exists(backup))
				{
					File.Move(backup, dest, overwrite: true);
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// The previous jar remains at its backup path for manual recovery.
				LauncherLog.Write("pin heal restore kept " + Path.GetFileName(backup ?? dest) + " " + ex.GetType().Name);
			}
		}
	}

	private static void TryDeleteQuietly(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("pin heal backup kept " + Path.GetFileName(path));
		}
	}

	public const long MaximumHealJarBytes = 268435456;

	private static async Task<bool> TryDownloadVerifiedAsync(HttpClient http, string name, string sha, long size, string explicitUrl, string temp, CancellationToken ct)
	{
		if (size <= 0 || size > MaximumHealJarBytes)
		{
			LauncherLog.Write("pin heal refuse size " + Path.GetFileName((name ?? "").Replace('\\', '/')));
			return false;
		}
		foreach (string url in JarUrls(name, explicitUrl))
		{
			try
			{
				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
				request.Headers.TryAddWithoutValidation("User-Agent", NorcomVersion.UserAgent);
				request.Headers.CacheControl = new CacheControlHeaderValue
				{
					NoCache = true,
					NoStore = true
				};
				if (url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
				{
					request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
				}
				LauncherLog.Write("download " + Path.GetFileName((name ?? "").Replace('\\', '/')));
				using CancellationTokenSource budget = HttpDownload.OpenBudget(ct);
				using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(continueOnCapturedContext: false);
				if (!response.IsSuccessStatusCode)
				{
					continue;
				}
				bool contentsApi = url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase);
				long limit = contentsApi ? (size / 3 + 1) * 4 + 1048576 : size;
				if (response.Content.Headers.ContentLength is long announced && announced > limit)
				{
					continue;
				}
				byte[]? body = await ReadBoundedAsync(response, limit, budget).ConfigureAwait(continueOnCapturedContext: false);
				if (body == null)
				{
					continue;
				}
				if (url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
				{
					byte[] decoded = DecodeContentsBytes(body);
					if (decoded == null)
					{
						continue;
					}
					body = decoded;
				}
				if (size > 0 && body.LongLength != size)
				{
					continue;
				}
				string actual = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
				if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				await File.WriteAllBytesAsync(temp, body, ct).ConfigureAwait(continueOnCapturedContext: false);
				return true;
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or JsonException or FormatException or TimeoutException)
			{
				if (ex is OperationCanceledException or TaskCanceledException && !ct.IsCancellationRequested)
				{
					LauncherLog.Write("download timed out " + Path.GetFileName((name ?? "").Replace('\\', '/')));
					return false;
				}
				LauncherLog.Write("pin heal fetch " + ex.GetType().Name);
			}
		}
		return false;
	}

	private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, long limit, CancellationTokenSource budget)
	{
		await using Stream input = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(continueOnCapturedContext: false);
		using MemoryStream output = new MemoryStream();
		byte[] buffer = new byte[81920];
		int read;
		while ((read = await input.ReadAsync(buffer, budget.Token).ConfigureAwait(continueOnCapturedContext: false)) > 0)
		{
			HttpDownload.KeepAlive(budget);
			if (output.Length + read > limit)
			{
				return null;
			}
			output.Write(buffer, 0, read);
		}
		return output.ToArray();
	}

	private static IEnumerable<string> JarUrls(string name, string explicitUrl)
	{
		if (!string.IsNullOrWhiteSpace(explicitUrl))
		{
			yield return explicitUrl.Trim();
			yield break;
		}
		string relative = EntryRelative(name);
		string escaped = string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
		string tip = string.IsNullOrWhiteSpace(PinFetch.ResolvedTip) ? PinFetch.KnownTip : PinFetch.ResolvedTip.Trim();
		yield return "https://raw.githubusercontent.com/" + PinFetch.Repo + "/" + tip + "/" + escaped;
		yield return "https://cdn.jsdelivr.net/gh/" + PinFetch.Repo + "@" + tip + "/" + escaped;
		yield return "https://api.github.com/repos/" + PinFetch.Repo + "/contents/" + escaped + "?ref=" + tip;
	}

	private static byte[] DecodeContentsBytes(byte[] body)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String)
			{
				return null;
			}
			if (root.TryGetProperty("encoding", out JsonElement encoding))
			{
				string kind = encoding.GetString() ?? "";
				if (kind.Length > 0 && !kind.Equals("base64", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
			}
			string b64 = (content.GetString() ?? "").Replace("\n", "").Replace("\r", "");
			if (b64.Length == 0)
			{
				return null;
			}
			return Convert.FromBase64String(b64);
		}
		catch (Exception ex) when (ex is JsonException or FormatException)
		{
			LauncherLog.Write("pin heal contents " + ex.GetType().Name);
			return null;
		}
	}

	private static string HealDest(string gameDir, string name)
	{
		return EntryPath(gameDir, name);
	}

	private static bool CommitApplied(string gameDir, string launcherDir, string pin, string generatedAt)
	{
		string text = pin.Trim();
		Dictionary<string, object> record = new Dictionary<string, object>
		{
			["schema"] = 1,
			["pin"] = text,
			["pin_sha256"] = text,
			["appliedUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
		};
		if (!string.IsNullOrWhiteSpace(generatedAt))
		{
			record["generated_at"] = generatedAt.Trim();
		}
		string json = JsonSerializer.Serialize(record);
		string primary = Path.Combine(launcherDir, PrimaryAppliedPinFile);
		string marker = Path.Combine(gameDir, AppliedPinFile);
		string primaryOld = File.Exists(primary) ? File.ReadAllText(primary) : null;
		string markerOld = File.Exists(marker) ? File.ReadAllText(marker) : null;
		try
		{
			WriteAtomic(primary, json);
			WriteAtomic(marker, text + Environment.NewLine);
			if (!string.Equals(ReadAppliedPin(gameDir), text, StringComparison.OrdinalIgnoreCase) || !string.Equals(ReadPrimaryAppliedPin(launcherDir), text, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException("stamp");
			}
			PublishedPin = text;
			LauncherLog.Write("pin applied " + text);
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pin applied failed " + ex.GetType().Name);
			RestoreText(primary, primaryOld);
			RestoreText(marker, markerOld);
			return false;
		}
	}

	private static void RestoreText(string path, string previous)
	{
		try
		{
			if (previous == null)
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				return;
			}
			File.WriteAllText(path, previous);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("pin restore " + ex.GetType().Name);
		}
	}

	private static void WriteAtomic(string path, string text)
	{
		string directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
		string tmp = path + ".tmp";
		File.WriteAllText(tmp, text);
		File.Move(tmp, path, overwrite: true);
	}

	private static bool TryPublished(string json, out string pin, out List<(string Name, string Sha256, long Size, string Url)> jars, out bool hasJars)
	{
		pin = "";
		jars = new List<(string, string, long, string)>();
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
				foreach (JsonElement jar in jarsElement.EnumerateArray())
				{
					string name = Required(jar, "name");
					string sha = Required(jar, "sha256");
					if (!jar.TryGetProperty("size", out JsonElement size) || !size.TryGetInt64(out long bytes))
					{
						return false;
					}
					jars.Add((name, sha, bytes, Optional(jar, "url") ?? ""));
				}
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
		return EntryPath(gameDir, name);
	}

	// A pin entry names one path. A bare file name is that jar under mods/. A same-named file in another folder is not this entry.
	private static string EntryRelative(string name)
	{
		string rel = (name ?? "").Replace('\\', '/').Trim();
		while (rel.StartsWith("./", StringComparison.Ordinal))
		{
			rel = rel.Substring(2);
		}
		rel = rel.TrimStart('/');
		if (rel.Length == 0 || rel.Contains("..", StringComparison.Ordinal) || rel.IndexOf(':') >= 0)
		{
			throw new InvalidDataException("Pin jar path is outside the game folder.");
		}
		if (rel.IndexOf('/') < 0)
		{
			rel = "mods/" + rel;
		}
		return rel;
	}

	private static string EntryPath(string gameDir, string name)
	{
		return PackSyncService.ConfinedPath(gameDir, EntryRelative(name));
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
