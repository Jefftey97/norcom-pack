using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class QaGates
{
    public static bool OfflineCheckRequested() => OptIn(Environment.GetEnvironmentVariable("NORCOM_STATE_DIR"),
        Environment.GetEnvironmentVariable("NORCOM_GAME_DIR"),Environment.GetEnvironmentVariable("NORCOM_DESKTOP"),
        Environment.GetEnvironmentVariable("NORCOM_QA_OFFLINE"));

    public static HttpClient OfflineClient() => new HttpClient(new OfflineHandler());
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("Network is disabled for this explicit offline verification profile."));
    }
	public const string FailReplaceVariable = "NORCOM_QA_FAIL_REPLACE";

	public const string DownloadJavaVariable = "NORCOM_QA_DOWNLOAD_JAVA";

	public static bool OptIn(string? stateDir, string? gameDir, string? desktop, string? flag)
	{
		if (!string.IsNullOrWhiteSpace(stateDir) && !string.IsNullOrWhiteSpace(gameDir) && !string.IsNullOrWhiteSpace(desktop))
		{
			return string.Equals(flag?.Trim(), "1", StringComparison.Ordinal);
		}
		return false;
	}

	public static bool FailReplaceRequested()
	{
		return OptIn(Environment.GetEnvironmentVariable("NORCOM_STATE_DIR"), Environment.GetEnvironmentVariable("NORCOM_GAME_DIR"), Environment.GetEnvironmentVariable("NORCOM_DESKTOP"), Environment.GetEnvironmentVariable("NORCOM_QA_FAIL_REPLACE"));
	}

	public static bool DownloadJavaRequested()
	{
		return OptIn(Environment.GetEnvironmentVariable("NORCOM_STATE_DIR"), Environment.GetEnvironmentVariable("NORCOM_GAME_DIR"), Environment.GetEnvironmentVariable("NORCOM_DESKTOP"), Environment.GetEnvironmentVariable("NORCOM_QA_DOWNLOAD_JAVA"));
	}
}
