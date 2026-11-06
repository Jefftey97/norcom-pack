using System;
using System.Collections.Generic;
using System.IO;
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

	public const string BlockCopy = "Pack doesn't match live server pin — wait for update. Repair before you play.";

	public const string MissingCopy = "Server pin is not published yet. Play will continue.";

	public static string? LastWarning { get; private set; }

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, CancellationToken ct)
	{
		LastWarning = null;
		foreach (string pinUrl in PinUrls)
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
		Warn("pin 404");
	}

	public static async Task EnforceAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string pinUrl, CancellationToken ct)
	{
		LastWarning = null;
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
				string reason = await DescribeMismatchAsync(document.RootElement, pack, gameDir, ct).ConfigureAwait(continueOnCapturedContext: false);
				string line = "pin " + pinUrl + " " + Summarize(document.RootElement) + (reason == null ? " ok" : " block " + reason);
				LauncherLog.Write(line);
				if (reason != null)
				{
					throw new PinMismatchException(BlockCopy);
				}
			}
		}
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
