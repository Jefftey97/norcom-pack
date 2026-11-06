namespace Norcom.Launcher.Core.Services;

public sealed class AuthResult
{
	public AuthFailure Failure { get; }

	public string Message { get; }

	public MinecraftSession? Session { get; }

	private AuthResult(AuthFailure failure, string message, MinecraftSession? session)
	{
		Failure = failure;
		Message = message;
		Session = session;
	}

	public static AuthResult Ok(MinecraftSession session)
	{
		return new AuthResult(AuthFailure.None, AuthMessages.SignedIn(session.Name), session);
	}

	public static AuthResult Fail(AuthFailure failure, string? message = null)
	{
		return new AuthResult(failure, message ?? AuthMessages.For(failure), null);
	}

	public override string ToString()
	{
		return Message;
	}
}
