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
	public const string ServerPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json";

	public const string CompatPinUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json";

	public const string HostPinPath = "G:\\MinecraftServer\\.norcom\\server-pin.json";

	public const string AppliedPinFile = "norcom-applied-pin.txt";

	public static readonly string[] PinUrls = new string[2] { "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-pin.json", "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/server-compat.json" };

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
		string packUrl = (string.IsNullOrWhiteSpace(text) ? DefaultPackUrl : text.Trim());
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
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pack.toml " + ex.GetType().Name);
			return;
		}
		await EnforceAsync(http, gameDir, TomlLite.Parse(body), ct, packUrl, manifestUrl).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct)
	{
		return EnforceAsync(http, gameDir, pack, ct, null, null);
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct, string? packUrl, string? manifestUrl, bool missingBlocks = true)
	{
		LastWarning = null;
		AcceptedPinJson = null;
		foreach (string pinUrl in CandidateUrls(packUrl, manifestUrl))
		{
			JsonDocument? parsed = await TryFetchPinAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (parsed == null)
			{
				continue;
			}
			using (parsed)
			{
				Judge(parsed.RootElement, gameDir, pack, pinUrl);
				return;
			}
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
		JsonDocument? parsed;
		try
		{
			parsed = await TryFetchPinAsync(http, pinUrl, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			parsed = null;
			if (MissingRefuses)
			{
				LauncherLog.Write("pin fetch failed required " + ex.GetType().Name);
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pin fetch failed " + ex.GetType().Name);
			return;
		}
		if (parsed == null)
		{
			if (MissingRefuses)
			{
				LauncherLog.Write("pin missing required " + pinUrl);
				throw new PinMismatchException(BlockCopy);
			}
			Warn("pin 404 " + pinUrl);
			return;
		}
		using (parsed)
		{
			Judge(parsed.RootElement, gameDir, pack, pinUrl);
		}
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

	private static void Judge(JsonElement pin, string gameDir, TomlLite.Doc pack, string pinUrl)
	{
		if (pin.ValueKind != JsonValueKind.Object || !pin.TryGetProperty("mc", out _) || !pin.TryGetProperty("loader", out _) || !pin.TryGetProperty("pin", out _))
		{
			throw new PinMismatchException("Server pin could not be read. Play was not started.");
		}
		string mc = Required(pin, "mc");
		string loader = Required(pin, "loader");
		string expectedPin = Required(pin, "pin");
		string packMc = pack.Keys.GetValueOrDefault("versions.minecraft") ?? pack.Keys.GetValueOrDefault("minecraft") ?? "";
		string packLoader = pack.Keys.GetValueOrDefault("versions.fabric") ?? pack.Keys.GetValueOrDefault("fabric") ?? pack.Keys.GetValueOrDefault("loader") ?? "";
		string actualPin = ReadAppliedPin(gameDir) ?? "";
		string line = "pin " + pinUrl + " expected mc=" + mc + " loader=" + loader + " pin=" + expectedPin + " actual mc=" + packMc + " loader=" + packLoader + " pin=" + actualPin;
		if (!SameToken(mc, packMc))
		{
			LauncherLog.Write(line + " block mc");
			throw new PinMismatchException(BlockCopy + " Expected mc " + mc + " pin " + expectedPin + ". Actual mc " + packMc + ".");
		}
		if (!SameToken(loader, packLoader))
		{
			LauncherLog.Write(line + " block loader");
			throw new PinMismatchException(BlockCopy + " Expected loader " + loader + " pin " + expectedPin + ". Actual loader " + packLoader + ".");
		}
		if (actualPin.Length > 0 && !string.Equals(actualPin, expectedPin, StringComparison.Ordinal))
		{
			LauncherLog.Write(line + " block pin");
			throw new PinMismatchException(BlockCopy + " Expected pin " + expectedPin + ". Actual pin " + actualPin + ".");
		}
		LauncherLog.Write(line + (actualPin.Length == 0 ? " accepted" : " ok"));
		AcceptedPinJson = "{\"schema\":1,\"mc\":" + JsonSerializer.Serialize(mc) + ",\"loader\":" + JsonSerializer.Serialize(loader) + ",\"pin\":" + JsonSerializer.Serialize(expectedPin) + ",\"source\":\"server\"}";
		Remember(gameDir, expectedPin);
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

	private static string Required(JsonElement pin, string name)
	{
		if (!pin.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
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
