namespace Norcom.Launcher.Core.Services;

public sealed class MicrosoftAccountResult
{
	public AuthFailure Failure { get; }

	public string? AccessToken { get; }

	public string Message { get; }

	private MicrosoftAccountResult(AuthFailure failure, string? accessToken, string message)
	{
		Failure = failure;
		AccessToken = accessToken;
		Message = message;
	}

	public static MicrosoftAccountResult Ok(string accessToken)
	{
		return new MicrosoftAccountResult(AuthFailure.None, accessToken, "");
	}

	public static MicrosoftAccountResult Fail(AuthFailure failure)
	{
		return new MicrosoftAccountResult(failure, null, AuthMessages.For(failure));
	}

	public override string ToString()
	{
		if (Failure != 0)
		{
			return Message;
		}
		return "microsoft-token";
	}
}
