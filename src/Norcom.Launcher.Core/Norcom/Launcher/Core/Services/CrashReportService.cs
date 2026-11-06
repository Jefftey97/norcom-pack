using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class CrashReportService
{
	public enum DeliveryKind
	{
		Saved,
		Sent,
		NoEndpoint,
		SendFailed,
		Queued
	}

	public readonly record struct Delivery(DeliveryKind Kind, string UserText);

	public const string SentMessage = "A report was sent to NORCOM so it can be fixed";

	public const string SavedNoEndpointMessage = "A report was saved on this PC. No crash channel is configured, so it was not sent.";

	public const string SavedFailedMessage = "A report was saved on this PC. Sending it to NORCOM failed. It will be tried again the next time NORCOM opens.";

	public const string QueuedMessage = "A report was saved for NORCOM and is queued to be sent. It has not been sent yet.";

	public const string UserMessage = "A report was sent to NORCOM so it can be fixed";

	public const string WebhookEnvironmentVariable = "NORCOM_CRASH_WEBHOOK";

	public static string Queue(string source, string message, string? stack)
	{
		return Queue(NorcomPaths.CrashOutbox, source, message, stack);
	}

	public static string Queue(string directory, string source, string message, string? stack)
	{
		Directory.CreateDirectory(directory);
		var value = new
		{
			product = "NORCOM Launcher",
			version = NorcomVersion.Current,
			source = source,
			message = Redactor.Clean(message),
			stack = Redactor.Clean(stack),
			utc = DateTimeOffset.UtcNow.ToString("o")
		};
		string text = Path.Combine(directory, "crash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
		string contents = JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		File.WriteAllText(text, contents);
		File.WriteAllText(text + ".status", DeliveryKind.Saved.ToString());
		LauncherLog.Write("Crash report stored locally (" + source + ").");
		return text;
	}

	public static Delivery Describe(bool posted, bool hadEndpoint)
	{
		if (!posted)
		{
			if (!hadEndpoint)
			{
				return new Delivery(DeliveryKind.NoEndpoint, "A report was saved on this PC. No crash channel is configured, so it was not sent.");
			}
			return new Delivery(DeliveryKind.SendFailed, "A report was saved on this PC. Sending it to NORCOM failed. It will be tried again the next time NORCOM opens.");
		}
		return new Delivery(DeliveryKind.Sent, "A report was sent to NORCOM so it can be fixed");
	}

	public static string? ResolveWebhook(string? environmentValue, string? settingsValue, string? fileText)
	{
		string[] array = new string[3] { environmentValue, fileText, settingsValue };
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i]?.Trim();
			if (IsWebhook(text))
			{
				return text;
			}
		}
		return null;
	}

	public static bool IsWebhook(string? url)
	{
		if (!string.IsNullOrWhiteSpace(url))
		{
			return url.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static Task<Delivery> TryDeliverAsync(HttpClient http, string reportPath, string? webhookUrl, CancellationToken cancellationToken)
	{
		return TryDeliverAsync(http, reportPath, webhookUrl, null, "", null, cancellationToken);
	}

	public static async Task<Delivery> TryDeliverAsync(HttpClient http, string reportPath, string? webhookUrl, string? ingestUrl, string? windowsVersion, string? logTail, CancellationToken cancellationToken)
	{
		if (!File.Exists(reportPath))
		{
			return new Delivery(DeliveryKind.SendFailed, "A report was saved on this PC. Sending it to NORCOM failed. It will be tried again the next time NORCOM opens.");
		}
		if (CrashIngest.IsAllowed(ingestUrl))
		{
			return await PostIngestAsync(http, reportPath, ingestUrl, windowsVersion, logTail, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!IsWebhook(webhookUrl))
		{
			WriteStatus(reportPath, DeliveryKind.NoEndpoint);
			LauncherLog.Write("Crash report kept locally. No crash channel is configured.");
			return Describe(posted: false, hadEndpoint: false);
		}
		string text = Redactor.Clean(await File.ReadAllTextAsync(reportPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
		if (text.Length > 3500)
		{
			text = text.Substring(0, 3500) + "…";
		}
		string content = JsonSerializer.Serialize(new
		{
			content = (string)null,
			embeds = new[]
			{
				new
				{
					title = "NORCOM launcher",
					description = text
				}
			}
		});
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
		request.Content = new StringContent(content, Encoding.UTF8, "application/json");
		request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NORCOM-Launcher", NorcomVersion.Current));
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(8.0));
		try
		{
			using HttpResponseMessage httpResponseMessage = await http.SendAsync(request, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			Delivery result = Describe(httpResponseMessage.IsSuccessStatusCode, hadEndpoint: true);
			WriteStatus(reportPath, result.Kind);
			LauncherLog.Write(httpResponseMessage.IsSuccessStatusCode ? "Crash report posted to the private channel." : ("Crash report stayed local. The channel returned " + (int)httpResponseMessage.StatusCode + "."));
			return result;
		}
		catch (Exception ex)
		{
			WriteStatus(reportPath, DeliveryKind.SendFailed);
			LauncherLog.Write("Crash report stayed local. " + ex.GetType().Name);
			return Describe(posted: false, hadEndpoint: true);
		}
	}

	public static async Task FlushAsync(HttpClient http, string outbox, string? webhookUrl, string? ingestUrl, string? windowsVersion, string? logTail, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(outbox) || (!CrashIngest.IsAllowed(ingestUrl) && !IsWebhook(webhookUrl)))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(outbox, "crash-*.json"))
		{
			string path = item + ".status";
			if (!(File.Exists(path) ? File.ReadAllText(path).Trim() : DeliveryKind.Saved.ToString()).Equals(DeliveryKind.Sent.ToString(), StringComparison.OrdinalIgnoreCase))
			{
				await TryDeliverAsync(http, item, webhookUrl, ingestUrl, windowsVersion, logTail, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
	}

	private static async Task<Delivery> PostIngestAsync(HttpClient http, string reportPath, string ingestUrl, string? windowsVersion, string? logTail, CancellationToken cancellationToken)
	{
		string content = CrashIngest.BuildBody(await File.ReadAllTextAsync(reportPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), windowsVersion ?? "", logTail);
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ingestUrl);
		request.Content = new StringContent(content, Encoding.UTF8, "application/json");
		request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NORCOM-Launcher", NorcomVersion.Current));
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(8.0));
		try
		{
			using HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			int statusCode = (int)response.StatusCode;
			if (statusCode >= 300 && statusCode < 400)
			{
				WriteStatus(reportPath, DeliveryKind.SendFailed);
				LauncherLog.Write("Crash report stayed local. The receiver redirected.");
				return Describe(posted: false, hadEndpoint: true);
			}
			string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
			Delivery result = ReadIngestStatus(response.IsSuccessStatusCode, (int)response.StatusCode, body) switch
			{
				DeliveryKind.Sent => new Delivery(DeliveryKind.Sent, "A report was sent to NORCOM so it can be fixed"), 
				DeliveryKind.Queued => new Delivery(DeliveryKind.Queued, "A report was saved for NORCOM and is queued to be sent. It has not been sent yet."), 
				DeliveryKind.NoEndpoint => Describe(posted: false, hadEndpoint: false), 
				_ => Describe(posted: false, hadEndpoint: true), 
			};
			WriteStatus(reportPath, result.Kind);
			LauncherLog.Write("Crash ingest " + result.Kind.ToString() + ".");
			return result;
		}
		catch (Exception ex)
		{
			WriteStatus(reportPath, DeliveryKind.SendFailed);
			LauncherLog.Write("Crash report stayed local. " + ex.GetType().Name);
			return Describe(posted: false, hadEndpoint: true);
		}
	}

	public static DeliveryKind ReadIngestStatus(bool success, int statusCode, string? body)
	{
		if (statusCode == 202)
		{
			return DeliveryKind.Queued;
		}
		if (!success)
		{
			return DeliveryKind.SendFailed;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body ?? "");
			JsonElement value;
			DeliveryKind result;
			switch (jsonDocument.RootElement.TryGetProperty("status", out value) ? value.GetString() : null)
			{
			case "forwarded":
			case "duplicate":
				result = DeliveryKind.Sent;
				break;
			case "queued":
				result = DeliveryKind.Queued;
				break;
			default:
				result = DeliveryKind.SendFailed;
				break;
			}
			return result;
		}
		catch
		{
			return DeliveryKind.SendFailed;
		}
	}

	private static void WriteStatus(string reportPath, DeliveryKind kind)
	{
		try
		{
			File.WriteAllText(reportPath + ".status", kind.ToString());
		}
		catch
		{
		}
	}
}
