using System;

namespace Norcom.Launcher.Core.Services;

public static class MsaClientId
{
	public const string EnvironmentVariable = "NORCOM_MSA_CLIENT_ID";

	public const string BorrowedLauncherId = "00000000402b5328";

	public static ClientIdDecision Parse(string? environment, string? fileText)
	{
		string text = (string.IsNullOrWhiteSpace(environment) ? fileText : environment);
		if (string.IsNullOrWhiteSpace(text))
		{
			return ClientIdDecision.Missing();
		}
		text = text.Trim().TrimStart('\ufeff');
		if (text.Contains("00000000402b5328", StringComparison.OrdinalIgnoreCase))
		{
			return ClientIdDecision.Borrowed();
		}
		if (!Guid.TryParse(text, out var result) || result == Guid.Empty)
		{
			return ClientIdDecision.Invalid();
		}
		return ClientIdDecision.Ready(result.ToString("D"));
	}
}
