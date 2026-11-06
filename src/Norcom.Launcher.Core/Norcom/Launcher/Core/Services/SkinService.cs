using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class SkinService
{
	private readonly HttpClient _http;

	public SkinService(HttpClient http)
	{
		_http = http;
	}

	public async Task<SkinUploadResult> UploadAsync(string pngPath, string variant, string accessToken, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(accessToken) || accessToken == "0")
		{
			return new SkinUploadResult(Ok: false, "Sign in with the official Minecraft Launcher on this PC, then try the upload again.");
		}
		if (!File.Exists(pngPath))
		{
			return new SkinUploadResult(Ok: false, "That skin file is missing.");
		}
		byte[] array = await File.ReadAllBytesAsync(pngPath, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (array.Length < 8 || array[0] != 137 || array[1] != 80)
		{
			return new SkinUploadResult(Ok: false, "Skins need to be a PNG.");
		}
		using MultipartFormDataContent form = new MultipartFormDataContent();
		form.Add(new StringContent(variant.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "slim" : "classic"), "variant");
		ByteArrayContent byteArrayContent = new ByteArrayContent(array);
		byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		form.Add(byteArrayContent, "file", "skin.png");
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/minecraft/profile/skins");
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		req.Content = form;
		try
		{
			using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (resp.IsSuccessStatusCode)
			{
				return new SkinUploadResult(Ok: true, "Skin uploaded to your Minecraft profile.");
			}
			string text = SummarizeError(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false));
			int statusCode = (int)resp.StatusCode;
			if ((statusCode == 401 || statusCode == 403) ? true : false)
			{
				return new SkinUploadResult(Ok: false, "Minecraft rejected the sign-in. Open the official launcher so the account refreshes, then try again.");
			}
			return new SkinUploadResult(Ok: false, "Minecraft skin API returned " + (int)resp.StatusCode + ((text.Length == 0) ? "." : (": " + text)));
		}
		catch (Exception ex)
		{
			return new SkinUploadResult(Ok: false, "Could not reach the Minecraft skin API. " + Redactor.Clean(ex.Message));
		}
	}

	public async Task<string?> DownloadActiveSkinAsync(string accessToken, string destPng, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(accessToken) || accessToken == "0")
		{
			return null;
		}
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
		req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!resp.IsSuccessStatusCode)
		{
			return null;
		}
		using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(continueOnCapturedContext: false));
		if (!doc.RootElement.TryGetProperty("skins", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return null;
		}
		string text = null;
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.TryGetProperty("state", out var value2) && value2.GetString() == "ACTIVE" && item.TryGetProperty("url", out var value3))
			{
				text = value3.GetString();
			}
		}
		if (text == null)
		{
			return null;
		}
		await HttpDownload.DownloadAsync(_http, text, destPng, new TransferMeter(), ct).ConfigureAwait(continueOnCapturedContext: false);
		return destPng;
	}

	private static string SummarizeError(string body)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			if (jsonDocument.RootElement.TryGetProperty("errorMessage", out var value))
			{
				return Redactor.Clean(value.GetString());
			}
		}
		catch
		{
		}
		string text = Redactor.Clean(body).Trim();
		if (text.Length <= 180)
		{
			return text;
		}
		return text.Substring(0, 180);
	}
}
