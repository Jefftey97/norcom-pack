namespace Norcom.Launcher.Core.Services;

public sealed class ExchangeResult
{
	public AuthFailure Failure { get; }

	public string Message { get; }

	public string? AccessToken { get; }

	public string? Name { get; }

	public string? Uuid { get; }

	public string? Xuid { get; }

	public int ExpiresIn { get; }

	private ExchangeResult(AuthFailure failure, string message, string? token, string? name, string? uuid, string? xuid, int expiresIn)
	{
		Failure = failure;
		Message = message;
		AccessToken = token;
		Name = name;
		Uuid = uuid;
		Xuid = xuid;
		ExpiresIn = expiresIn;
	}

	public static ExchangeResult Ok(string token, string name, string uuid, string xuid, int expiresIn)
	{
		return new ExchangeResult(AuthFailure.None, AuthMessages.SignedIn(name), token, name, uuid, xuid, expiresIn);
	}

	public static ExchangeResult Fail(AuthFailure failure, string? message = null)
	{
		return new ExchangeResult(failure, message ?? AuthMessages.For(failure), null, null, null, null, 0);
	}

	public override string ToString()
	{
		return Message;
	}
}
