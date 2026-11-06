using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public sealed class MinecraftServicesAuth
{
	private sealed class HttpJson
	{
		public AuthFailure Failure { get; }

		public string Message { get; }

		public JsonDocument? Json { get; }

		private HttpJson(AuthFailure failure, string message, JsonDocument? json)
		{
			Failure = failure;
			Message = message;
			Json = json;
		}

		public static HttpJson Ok(JsonDocument json)
		{
			return new HttpJson(AuthFailure.None, "", json);
		}

		public static HttpJson Fail(AuthFailure failure, string? message = null)
		{
			return new HttpJson(failure, message ?? AuthMessages.For(failure), null);
		}
	}

	private readonly HttpClient _http;

	public MinecraftServicesAuth(HttpClient http)
	{
		_http = http;
	}

	public static HttpClient CreateHttp()
	{
		HttpClient httpClient = new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		});
		httpClient.Timeout = TimeSpan.FromSeconds(30.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(NorcomVersion.UserAgent);
		return httpClient;
	}

	public async Task<ExchangeResult> ExchangeAsync(string microsoftAccessToken, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(microsoftAccessToken))
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		JsonObject body = new JsonObject
		{
			["Properties"] = new JsonObject
			{
				["AuthMethod"] = "RPS",
				["SiteName"] = "user.auth.xboxlive.com",
				["RpsTicket"] = "d=" + microsoftAccessToken
			},
			["RelyingParty"] = "http://auth.xboxlive.com",
			["TokenType"] = "JWT"
		};
		HttpJson httpJson = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", body, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (httpJson.Failure != 0)
		{
			return ExchangeResult.Fail(httpJson.Failure, httpJson.Message);
		}
		if (!TryToken(httpJson.Json, out string token, out string userHash, out string _))
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		JsonObject jsonObject = new JsonObject();
		jsonObject["Properties"] = new JsonObject
		{
			["SandboxId"] = "RETAIL",
			["UserTokens"] = new JsonArray(JsonValue.Create(token))
		};
		jsonObject["RelyingParty"] = "rp://api.minecraftservices.com/";
		jsonObject["TokenType"] = "JWT";
		JsonObject body2 = jsonObject;
		HttpJson httpJson2 = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", body2, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (httpJson2.Failure != 0)
		{
			return ExchangeResult.Fail(httpJson2.Failure, httpJson2.Message);
		}
		if (!TryToken(httpJson2.Json, out string token2, out string uhs, out string xuid))
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		string text = (string.IsNullOrEmpty(uhs) ? userHash : uhs);
		if (!Digits(text, 20))
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		JsonObject body3 = new JsonObject { ["identityToken"] = "XBL3.0 x=" + text + ";" + token2 };
		HttpJson httpJson3 = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", body3, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (httpJson3.Failure != 0)
		{
			return ExchangeResult.Fail(httpJson3.Failure, httpJson3.Message);
		}
		if (httpJson3.Json == null || !httpJson3.Json.RootElement.TryGetProperty("access_token", out var value))
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		string access = value.GetString() ?? "";
		JsonElement value2;
		int value3;
		int expiresIn = ((httpJson3.Json.RootElement.TryGetProperty("expires_in", out value2) && value2.TryGetInt32(out value3)) ? value3 : 0);
		if (access.Length < 20 || expiresIn < 60)
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		HttpJson httpJson4 = await GetJsonAsync("https://api.minecraftservices.com/entitlements/mcstore", access, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (httpJson4.Failure != 0)
		{
			return ExchangeResult.Fail(httpJson4.Failure, httpJson4.Message);
		}
		if (!OwnsJava(httpJson4.Json))
		{
			return ExchangeResult.Fail(AuthFailure.NoEntitlement);
		}
		HttpJson httpJson5 = await GetJsonAsync("https://api.minecraftservices.com/minecraft/profile", access, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (httpJson5.Failure != 0)
		{
			return ExchangeResult.Fail(httpJson5.Failure, httpJson5.Message);
		}
		if (httpJson5.Json == null)
		{
			return ExchangeResult.Fail(AuthFailure.Rejected);
		}
		JsonElement value4;
		string uuid = (httpJson5.Json.RootElement.TryGetProperty("id", out value4) ? (value4.GetString() ?? "") : "");
		JsonElement value5;
		MinecraftSession minecraftSession = OnlineSession.Create(httpJson5.Json.RootElement.TryGetProperty("name", out value5) ? (value5.GetString() ?? "") : "", uuid, access, Digits(xuid, 32) ? xuid : "");
		if (!OnlineSession.IsPlayable(minecraftSession))
		{
			return ExchangeResult.Fail(AuthFailure.NoProfile);
		}
		return ExchangeResult.Ok(minecraftSession.AccessToken, minecraftSession.Name, minecraftSession.Uuid, minecraftSession.Xuid, expiresIn);
	}

	private static bool OwnsJava(JsonDocument? json)
	{
		if (json == null || !json.RootElement.TryGetProperty("items", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			JsonElement value2;
			string text = (item.TryGetProperty("name", out value2) ? value2.GetString() : null);
			if ((text == "product_minecraft" || text == "game_minecraft") ? true : false)
			{
				return true;
			}
		}
		return false;
	}

	private async Task<HttpJson> PostJsonAsync(string url, JsonObject body, string? bearer, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
		request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
		return await SendAsync(request, bearer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<HttpJson> GetJsonAsync(string url, string bearer, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
		return await SendAsync(request, bearer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<HttpJson> SendAsync(HttpRequestMessage request, string? bearer, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrEmpty(bearer))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		}
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		HttpResponseMessage response;
		try
		{
			response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return HttpJson.Fail(AuthFailure.Cancelled);
		}
		catch (Exception ex2) when (((ex2 is HttpRequestException || ex2 is TaskCanceledException || ex2 is OperationCanceledException) ? 1 : 0) != 0)
		{
			return HttpJson.Fail(AuthFailure.Network);
		}
		using (response)
		{
			int statusCode = (int)response.StatusCode;
			if (statusCode >= 300 && statusCode < 400)
			{
				return HttpJson.Fail(AuthFailure.Network);
			}
			string text;
			try
			{
				text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return HttpJson.Fail(AuthFailure.Cancelled);
			}
			catch
			{
				return HttpJson.Fail(AuthFailure.Network);
			}
			if (text.Length > 65536)
			{
				text = text.Substring(0, 65536);
			}
			return Interpret(request.RequestUri?.AbsolutePath ?? "", response.StatusCode, text);
		}
	}

	private static HttpJson Interpret(string path, HttpStatusCode status, string text)
	{
		JsonDocument jsonDocument = null;
		if (text.Length > 0 && text.TrimStart().StartsWith('{'))
		{
			try
			{
				jsonDocument = JsonDocument.Parse(text);
			}
			catch
			{
				jsonDocument = null;
			}
		}
		if (status == HttpStatusCode.Unauthorized && jsonDocument != null && jsonDocument.RootElement.TryGetProperty("XErr", out var value))
		{
			long value2;
			long num = (value.TryGetInt64(out value2) ? value2 : 0);
			long num2 = num - 2148916233u;
			if ((ulong)num2 <= 5uL)
			{
				switch (num2)
				{
				case 0L:
					return HttpJson.Fail(AuthFailure.XboxNoAccount);
				case 2L:
					return HttpJson.Fail(AuthFailure.XboxUnavailable);
				case 3L:
				case 4L:
					return HttpJson.Fail(AuthFailure.XboxAdult);
				case 5L:
					return HttpJson.Fail(AuthFailure.XboxChild);
				}
			}
			return HttpJson.Fail(AuthFailure.XboxRejected, AuthMessages.XboxCode(num));
		}
		if (status == HttpStatusCode.Forbidden && jsonDocument != null)
		{
			if (string.Equals(jsonDocument.RootElement.TryGetProperty("error", out var value3) ? value3.GetString() : null, "Invalid app registration", StringComparison.OrdinalIgnoreCase))
			{
				return HttpJson.Fail(AuthFailure.MojangNotApproved);
			}
			return HttpJson.Fail(AuthFailure.Rejected);
		}
		if (status == HttpStatusCode.NotFound && path.Contains("/minecraft/profile", StringComparison.Ordinal))
		{
			return HttpJson.Fail(AuthFailure.NoProfile);
		}
		if (status != HttpStatusCode.OK || jsonDocument == null)
		{
			return HttpJson.Fail(AuthFailure.Rejected);
		}
		return HttpJson.Ok(jsonDocument);
	}

	private static bool TryToken(JsonDocument? json, out string token, out string uhs, out string xuid)
	{
		token = "";
		uhs = "";
		xuid = "";
		if (json == null || !json.RootElement.TryGetProperty("Token", out var value))
		{
			return false;
		}
		token = value.GetString() ?? "";
		if (token.Length < 20 || token.Any(char.IsWhiteSpace))
		{
			return false;
		}
		if (!json.RootElement.TryGetProperty("DisplayClaims", out var value2))
		{
			return false;
		}
		if (!value2.TryGetProperty("xui", out var value3) || value3.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		foreach (JsonElement item in value3.EnumerateArray())
		{
			if (item.TryGetProperty("uhs", out var value4))
			{
				uhs = value4.GetString() ?? "";
			}
			if (item.TryGetProperty("xid", out var value5))
			{
				xuid = value5.GetString() ?? "";
			}
			if (uhs.Length > 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool Digits(string? value, int max)
	{
		if (string.IsNullOrEmpty(value) || value.Length > max)
		{
			return false;
		}
		for (int i = 0; i < value.Length; i++)
		{
			if (!char.IsDigit(value[i]))
			{
				return false;
			}
		}
		return true;
	}
}
