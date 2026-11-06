namespace Norcom.Launcher.Core.Services;

public sealed class ClientIdDecision
{
	public AuthFailure Failure { get; }

	public string? ClientId { get; }

	public string Message { get; }

	private ClientIdDecision(AuthFailure failure, string? clientId, string message)
	{
		Failure = failure;
		ClientId = clientId;
		Message = message;
	}

	public static ClientIdDecision Missing()
	{
		return new ClientIdDecision(AuthFailure.NotConfigured, null, "NORCOM Microsoft sign-in is not configured. Add the NORCOM app id before signing in. Play was not started.");
	}

	public static ClientIdDecision Invalid()
	{
		return new ClientIdDecision(AuthFailure.InvalidClientId, null, "The NORCOM app id is not a valid public client id. Play was not started.");
	}

	public static ClientIdDecision Borrowed()
	{
		return new ClientIdDecision(AuthFailure.InvalidClientId, null, "That id belongs to the official Minecraft launcher. NORCOM cannot use it. Play was not started.");
	}

	public static ClientIdDecision Ready(string clientId)
	{
		return new ClientIdDecision(AuthFailure.None, clientId, "");
	}
}
