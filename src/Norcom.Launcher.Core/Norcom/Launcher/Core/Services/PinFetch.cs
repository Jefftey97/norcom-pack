using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public readonly struct PinSource
{
	public PinSource(string kind, string url, bool contents)
	{
		Kind = kind;
		Url = url;
		Contents = contents;
	}

	public string Kind { get; }

	public string Url { get; }

	public bool Contents { get; }
}

public static class PinFetch
{
	public const string Repo = "Jefftey97/norcom-pack";

	public const string KnownTip = "e8b586664e6d9cfd0e2d49bd338c0b02ae797739";

	private static readonly string[] MetaFiles = new string[4] { "norcom-mod-pin.json", "server-pin.json", "server-compat.json", "heal-hint.json" };

	private static string _resolvedTip = KnownTip;

	private static string _etag = "";

	private static DateTime _nextTipUtc = DateTime.MinValue;

	// Conditional GitHub API requests answered 304 do not spend the anonymous
	// 60-per-hour budget, so a minute-by-minute poll keeps the authoritative
	// source instead of drifting to cached or archival copies.
	private static readonly Dictionary<string, (string ETag, string Body)> ContentsCache = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

	private static readonly object ContentsGate = new object();

	public static string ResolvedTip => _resolvedTip;

	public static string BareRawMain(string file)
	{
		return "https://raw.githubusercontent.com/" + Repo + "/main/meta/" + file;
	}

	public static IReadOnlyList<PinSource> Sources(string file, string tip, string bust)
	{
		string sha = string.IsNullOrWhiteSpace(tip) ? KnownTip : tip.Trim();
		string token = string.IsNullOrWhiteSpace(bust) ? "0" : bust.Trim();
		// Published routes first. The baked commit is only a last resort: a
		// rate-limited contents API must not stop on a stale KnownTip pin.
		List<PinSource> sources = new List<PinSource>
		{
			new PinSource("contents-api", "https://api.github.com/repos/" + Repo + "/contents/meta/" + file + "?ref=main", contents: true)
		};
		if (!sha.Equals(KnownTip, StringComparison.OrdinalIgnoreCase))
		{
			sources.Add(new PinSource("commit-ref", "https://raw.githubusercontent.com/" + Repo + "/" + sha + "/meta/" + file, contents: false));
		}
		sources.Add(new PinSource("raw-busted", "https://raw.githubusercontent.com/" + Repo + "/main/meta/" + file + "?t=" + token, contents: false));
		sources.Add(new PinSource("jsdelivr", "https://cdn.jsdelivr.net/gh/" + Repo + "@main/meta/" + file, contents: false));
		sources.Add(new PinSource("commit-ref", "https://raw.githubusercontent.com/" + Repo + "/" + KnownTip + "/meta/" + file, contents: false));
		return sources;
	}

	public static void ResetForTests()
	{
		_resolvedTip = KnownTip;
		_etag = "";
		_nextTipUtc = DateTime.MinValue;
		lock (ContentsGate)
		{
			ContentsCache.Clear();
		}
	}

	public static Task<string?> FetchNamedAsync(HttpClient http, string file, CancellationToken ct, bool forceTip)
	{
		return FetchNamedAsync(http, file, ct, forceTip, null);
	}

	public static async Task<string?> FetchNamedAsync(HttpClient http, string file, CancellationToken ct, bool forceTip, string? launcherDir)
	{
		await RefreshTipAsync(http, ct, forceTip).ConfigureAwait(continueOnCapturedContext: false);
		string bust = DateTime.UtcNow.Ticks.ToString();
		string? stale = null;
		foreach (PinSource source in Sources(file, _resolvedTip, bust))
		{
			string? body = await TryGetAsync(http, source, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(body))
			{
				continue;
			}
			if (source.Contents)
			{
				string? decoded = DecodeContents(body);
				if (!string.IsNullOrWhiteSpace(decoded))
				{
					body = decoded;
				}
			}
			if (!LooksLikePin(body))
			{
				continue;
			}
			if (file.Equals("norcom-mod-pin.json", StringComparison.OrdinalIgnoreCase) && PinGate.IsOlderThanApplied(body, launcherDir))
			{
				// Never offered as a fallback: it would roll this PC back.
				LauncherLog.Write("pin fetch older-than-applied " + source.Kind + " " + file);
				continue;
			}
			if (IsPinDocument(file) && PinGate.IsDowngradeDocument(body))
			{
				LauncherLog.Write("pin fetch stale " + source.Kind + " " + file);
				if (stale == null)
				{
					stale = body;
				}
				continue;
			}
			LauncherLog.Write("pin fetch " + source.Kind + " " + file);
			return body;
		}
		return stale;
	}

	public static async Task<string?> FetchCandidateAsync(HttpClient http, string url, CancellationToken ct)
	{
		string? file = MetaFile(url);
		if (file != null && UsesSafeSources(url))
		{
			return await FetchNamedAsync(http, file, ct, forceTip: false).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await TryGetRawAsync(http, url, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	public static bool UsesSafeSources(string url)
	{
		if (MetaFile(url) == null)
		{
			return false;
		}
		return url.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase) || url.Contains("api.github.com", StringComparison.OrdinalIgnoreCase);
	}

	public static string? DecodeContents(string body)
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
				if (!kind.Equals("base64", StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
			}
			string b64 = (content.GetString() ?? "").Replace("\n", "").Replace("\r", "");
			if (b64.Length == 0)
			{
				return null;
			}
			return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
		}
		catch (Exception ex) when (ex is JsonException or FormatException)
		{
			return null;
		}
	}

	public static bool LooksLikePin(string? body)
	{
		if (string.IsNullOrWhiteSpace(body))
		{
			return false;
		}
		return body.Contains("\"pin_sha256\"", StringComparison.OrdinalIgnoreCase) || body.Contains("\"pin\"", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task RefreshTipAsync(HttpClient http, CancellationToken ct, bool force)
	{
		if (!force && DateTime.UtcNow < _nextTipUtc)
		{
			return;
		}
		_nextTipUtc = DateTime.UtcNow.AddSeconds(20.0);
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repo + "/commits/main");
			request.Headers.TryAddWithoutValidation("User-Agent", NorcomVersion.UserAgent);
			request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
			request.Headers.CacheControl = new CacheControlHeaderValue
			{
				NoCache = true
			};
			if (_etag.Length > 0)
			{
				request.Headers.TryAddWithoutValidation("If-None-Match", _etag);
			}
			using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (response.StatusCode == HttpStatusCode.NotModified)
			{
				return;
			}
			if (!response.IsSuccessStatusCode)
			{
				return;
			}
			EntityTagHeaderValue etag = response.Headers.ETag;
			if (etag != null)
			{
				_etag = etag.Tag ?? "";
			}
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			using JsonDocument document = JsonDocument.Parse(body);
			if (!document.RootElement.TryGetProperty("sha", out JsonElement sha) || sha.ValueKind != JsonValueKind.String)
			{
				return;
			}
			string text = (sha.GetString() ?? "").Trim();
			if (text.Length >= 7 && IsHex(text))
			{
				_resolvedTip = text;
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
		}
	}

	private static async Task<string?> TryGetAsync(HttpClient http, PinSource source, CancellationToken ct)
	{
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, source.Url);
			request.Headers.TryAddWithoutValidation("User-Agent", NorcomVersion.UserAgent);
			request.Headers.CacheControl = new CacheControlHeaderValue
			{
				NoCache = true,
				NoStore = true
			};
			request.Headers.Pragma.ParseAdd("no-cache");
			(string ETag, string Body) cached = default;
			bool hasCached = false;
			if (source.Contents)
			{
				request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
				lock (ContentsGate)
				{
					hasCached = ContentsCache.TryGetValue(source.Url, out cached);
				}
				if (hasCached)
				{
					request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
				}
			}
			using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (source.Contents && hasCached && response.StatusCode == HttpStatusCode.NotModified)
			{
				return cached.Body;
			}
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(body))
			{
				return null;
			}
			string? tag = response.Headers.ETag?.Tag;
			if (source.Contents && !string.IsNullOrWhiteSpace(tag) && body.Length <= 1048576)
			{
				lock (ContentsGate)
				{
					ContentsCache[source.Url] = (tag, body);
				}
			}
			return body;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			return null;
		}
	}

	private static async Task<string?> TryGetRawAsync(HttpClient http, string url, CancellationToken ct)
	{
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.TryAddWithoutValidation("User-Agent", NorcomVersion.UserAgent);
			request.Headers.CacheControl = new CacheControlHeaderValue
			{
				NoCache = true
			};
			using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(continueOnCapturedContext: false);
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

	private static bool IsPinDocument(string file)
	{
		return file.Equals("norcom-mod-pin.json", StringComparison.OrdinalIgnoreCase) || file.Equals("server-pin.json", StringComparison.OrdinalIgnoreCase) || file.Equals("server-compat.json", StringComparison.OrdinalIgnoreCase);
	}

	private static string? MetaFile(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}
		int query = url.IndexOf('?');
		string path = query >= 0 ? url.Substring(0, query) : url;
		int slash = path.LastIndexOf('/');
		string name = slash >= 0 ? path.Substring(slash + 1) : path;
		for (int i = 0; i < MetaFiles.Length; i++)
		{
			if (name.Equals(MetaFiles[i], StringComparison.OrdinalIgnoreCase))
			{
				return MetaFiles[i];
			}
		}
		return null;
	}

	private static bool IsHex(string value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
			if (!hex)
			{
				return false;
			}
		}
		return true;
	}
}
