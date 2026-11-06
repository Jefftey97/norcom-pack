using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public sealed class NorcomSignIn
{
	private readonly IMicrosoftAccount _microsoft;

	private readonly MinecraftServicesAuth _services;

	private readonly IAuthSecretStore _store;

	private readonly string _authDirectory;

	private readonly Func<string?> _environment;

	private readonly Func<DateTimeOffset> _clock;

	public NorcomSignIn(IMicrosoftAccount microsoft, MinecraftServicesAuth services, IAuthSecretStore store, string authDirectory, Func<string?>? environment = null, Func<DateTimeOffset>? clock = null)
	{
		_microsoft = microsoft;
		_services = services;
		_store = store;
		_authDirectory = authDirectory;
		_environment = environment ?? ((Func<string>)(() => Environment.GetEnvironmentVariable("NORCOM_MSA_CLIENT_ID")));
		_clock = clock ?? ((Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow));
	}

	public AuthSnapshot RestoreStored()
	{
		ClientIdDecision clientIdDecision = CurrentClient();
		if (clientIdDecision.Failure != 0)
		{
			return new AuthSnapshot
			{
				SignedIn = false,
				Message = clientIdDecision.Message
			};
		}
		(MinecraftSession, DateTimeOffset)? tuple = Load();
		if (!tuple.HasValue)
		{
			return new AuthSnapshot
			{
				SignedIn = false,
				Message = "Sign in with Microsoft in Settings. NORCOM does not read the official launcher account, and Play does not start without a verified Minecraft profile."
			};
		}
		if (tuple.Value.Item2 <= _clock())
		{
			return new AuthSnapshot
			{
				SignedIn = true,
				Message = AuthMessages.Expired(tuple.Value.Item1.Name)
			};
		}
		return new AuthSnapshot
		{
			SignedIn = true,
			Message = AuthMessages.SignedIn(tuple.Value.Item1.Name)
		};
	}

	public async Task<AuthResult> SignInAsync(MicrosoftPrompt prompt, Action<DeviceCodePrompt>? deviceCode, CancellationToken cancellationToken)
	{
		if (prompt == MicrosoftPrompt.Silent)
		{
			return AuthResult.Fail(AuthFailure.Rejected);
		}
		ClientIdDecision clientIdDecision = CurrentClient();
		if (clientIdDecision.Failure != 0 || clientIdDecision.ClientId == null)
		{
			return AuthResult.Fail(clientIdDecision.Failure, clientIdDecision.Message);
		}
		ForgetCacheIfClientChanged(clientIdDecision.ClientId);
		File.WriteAllText(Path.Combine(_authDirectory, "bound-client.txt"), clientIdDecision.ClientId);
		MicrosoftAccountResult microsoftAccountResult;
		try
		{
			microsoftAccountResult = await _microsoft.AcquireAsync(clientIdDecision.ClientId, prompt, deviceCode, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			return AuthResult.Fail(AuthFailure.Cancelled);
		}
		if (microsoftAccountResult.Failure != 0 || string.IsNullOrEmpty(microsoftAccountResult.AccessToken))
		{
			return AuthResult.Fail(microsoftAccountResult.Failure);
		}
		_store.Delete("session.bin");
		return await FinishExchangeAsync(microsoftAccountResult.AccessToken, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<AuthResult> ForPlayAsync(CancellationToken cancellationToken)
	{
		ClientIdDecision clientIdDecision = CurrentClient();
		if (clientIdDecision.Failure != 0 || clientIdDecision.ClientId == null)
		{
			return AuthResult.Fail(clientIdDecision.Failure, clientIdDecision.Message);
		}
		(MinecraftSession, DateTimeOffset)? tuple = Load();
		if (tuple.HasValue && tuple.Value.Item2 > _clock().AddMinutes(5.0))
		{
			return AuthResult.Ok(tuple.Value.Item1);
		}
		MicrosoftAccountResult microsoftAccountResult;
		try
		{
			microsoftAccountResult = await _microsoft.AcquireAsync(clientIdDecision.ClientId, MicrosoftPrompt.Silent, null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			return AuthResult.Fail(AuthFailure.Cancelled);
		}
		if (microsoftAccountResult.Failure != 0 || string.IsNullOrEmpty(microsoftAccountResult.AccessToken))
		{
			if (microsoftAccountResult.Failure == AuthFailure.InteractionRequired)
			{
				_store.Delete("session.bin");
			}
			return AuthResult.Fail((microsoftAccountResult.Failure == AuthFailure.None) ? AuthFailure.InteractionRequired : microsoftAccountResult.Failure);
		}
		return await FinishExchangeAsync(microsoftAccountResult.AccessToken, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SignOutAsync(CancellationToken cancellationToken)
	{
		ClientIdDecision clientIdDecision = CurrentClient();
		try
		{
			if (clientIdDecision.ClientId != null)
			{
				await _microsoft.SignOutAsync(clientIdDecision.ClientId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
		}
		finally
		{
			_store.Delete("session.bin");
			_store.Delete("msal.bin");
		}
	}

	private async Task<AuthResult> FinishExchangeAsync(string microsoftAccessToken, CancellationToken cancellationToken)
	{
		ExchangeResult exchangeResult;
		try
		{
			exchangeResult = await _services.ExchangeAsync(microsoftAccessToken, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			return AuthResult.Fail(AuthFailure.Cancelled);
		}
		if (exchangeResult.Failure != 0 || exchangeResult.AccessToken == null || exchangeResult.Name == null || exchangeResult.Uuid == null)
		{
			if (ClearsSession(exchangeResult.Failure))
			{
				_store.Delete("session.bin");
			}
			return AuthResult.Fail(exchangeResult.Failure, exchangeResult.Message);
		}
		MinecraftSession minecraftSession = OnlineSession.Create(exchangeResult.Name, exchangeResult.Uuid, exchangeResult.AccessToken, exchangeResult.Xuid ?? "");
		if (!OnlineSession.IsPlayable(minecraftSession) || minecraftSession.AccessToken == "0")
		{
			return AuthResult.Fail(AuthFailure.Rejected);
		}
		DateTimeOffset expires = _clock().AddSeconds(exchangeResult.ExpiresIn);
		_store.Write("session.bin", SessionCodec.Write(minecraftSession, expires));
		return AuthResult.Ok(minecraftSession);
	}

	private ClientIdDecision CurrentClient()
	{
		return MsaClientId.Parse(_environment(), AuthFiles.ReadClientId(_authDirectory));
	}

	private void ForgetCacheIfClientChanged(string clientId)
	{
		string path = Path.Combine(_authDirectory, "bound-client.txt");
		if (File.Exists(path) && !string.Equals(File.ReadAllText(path).Trim(), clientId, StringComparison.OrdinalIgnoreCase))
		{
			_store.Delete("session.bin");
			_store.Delete("msal.bin");
		}
	}

	private (MinecraftSession Session, DateTimeOffset Expires)? Load()
	{
		byte[] array = _store.Read("session.bin");
		if (array == null || !SessionCodec.TryRead(array, out MinecraftSession session, out DateTimeOffset expires))
		{
			return null;
		}
		return (session, expires);
	}

	private static bool ClearsSession(AuthFailure failure)
	{
		if ((uint)(failure - 5) <= 7u)
		{
			return true;
		}
		return false;
	}
}
