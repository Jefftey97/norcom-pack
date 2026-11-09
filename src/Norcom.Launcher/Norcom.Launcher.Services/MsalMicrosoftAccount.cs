using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Services;

public sealed class MsalMicrosoftAccount : IMicrosoftAccount
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static LogCallback _003C_003E9__4_0;

		internal void _003CBuild_003Eb__4_0(LogLevel level, string message, bool pii)
		{
		}
	}

	private readonly IAuthSecretStore _store;

	public MsalMicrosoftAccount(IAuthSecretStore store)
	{
		_store = store;
	}

	public async Task<MicrosoftAccountResult> AcquireAsync(string clientId, MicrosoftPrompt prompt, Action<DeviceCodePrompt>? deviceCode, CancellationToken cancellationToken)
	{
		IPublicClientApplication val = Build(clientId);
		try
		{
			AuthenticationResult val2 = (AuthenticationResult)(prompt switch
			{
				MicrosoftPrompt.Silent => await SilentAsync(val, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), 
				MicrosoftPrompt.DeviceCode => await ((BaseAbstractAcquireTokenParameterBuilder<AcquireTokenWithDeviceCodeParameterBuilder>)(object)val.AcquireTokenWithDeviceCode((IEnumerable<string>)MicrosoftSignInRoute.Scopes, (Func<DeviceCodeResult, Task>)delegate(DeviceCodeResult code)
				{
					deviceCode?.Invoke(new DeviceCodePrompt(code.UserCode, string.IsNullOrWhiteSpace(code.VerificationUrl) ? "https://www.microsoft.com/link" : code.VerificationUrl));
					return Task.CompletedTask;
				})).ExecuteAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false), 
				_ => await ((BaseAbstractAcquireTokenParameterBuilder<AcquireTokenInteractiveParameterBuilder>)(object)val.AcquireTokenInteractive((IEnumerable<string>)MicrosoftSignInRoute.Scopes).WithUseEmbeddedWebView(false)).ExecuteAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false), 
			});
			if (string.IsNullOrWhiteSpace(val2.AccessToken))
			{
				return MicrosoftAccountResult.Fail(AuthFailure.Rejected);
			}
			return MicrosoftAccountResult.Ok(val2.AccessToken);
		}
		catch (AuthStoreUnavailableException)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.StorageUnavailable);
		}
		catch (MsalUiRequiredException)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.InteractionRequired);
		}
		catch (MsalClientException ex2) when (ex2.ErrorCode == "authentication_canceled" || ex2.ErrorCode == "access_denied")
		{
			return MicrosoftAccountResult.Fail(AuthFailure.Cancelled);
		}
		catch (MsalServiceException ex3) when (ex3.StatusCode == 0)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.Network);
		}
		catch (HttpRequestException)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.Network);
		}
		catch (OperationCanceledException)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.Cancelled);
		}
		catch (MsalException)
		{
			return MicrosoftAccountResult.Fail(AuthFailure.Rejected);
		}
	}

	public async Task SignOutAsync(string clientId, CancellationToken cancellationToken)
	{
		IPublicClientApplication app = Build(clientId);
		foreach (IAccount item in await ((IClientApplicationBase)app).GetAccountsAsync().ConfigureAwait(continueOnCapturedContext: false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ((IClientApplicationBase)app).RemoveAsync(item).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private IPublicClientApplication Build(string clientId)
	{
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Expected O, but got Unknown
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Expected O, but got Unknown
		PublicClientApplicationBuilder obj = ((AbstractApplicationBuilder<PublicClientApplicationBuilder>)(object)((AbstractApplicationBuilder<PublicClientApplicationBuilder>)(object)PublicClientApplicationBuilder.Create(clientId)).WithAuthority("https://login.microsoftonline.com/consumers", true)).WithRedirectUri("http://localhost");
		object obj2 = _003C_003Ec._003C_003E9__4_0;
		if (obj2 == null)
		{
			LogCallback val = delegate
			{
			};
			_003C_003Ec._003C_003E9__4_0 = val;
			obj2 = (object)val;
		}
		IPublicClientApplication obj3 = ((BaseAbstractApplicationBuilder<PublicClientApplicationBuilder>)(object)obj).WithLogging((LogCallback)obj2, (LogLevel?)(LogLevel)0, (bool?)false, (bool?)false).Build();
		ProtectedTokenCache cache = new ProtectedTokenCache(_store);
		((IClientApplicationBase)obj3).UserTokenCache.SetBeforeAccess((TokenCacheCallback)delegate(TokenCacheNotificationArgs args)
		{
			cache.BeforeAccess(bytes => args.TokenCache.DeserializeMsalV3(bytes, true));
		});
		((IClientApplicationBase)obj3).UserTokenCache.SetAfterAccess((TokenCacheCallback)delegate(TokenCacheNotificationArgs args)
		{
			cache.AfterAccess(args.HasStateChanged, () => args.TokenCache.SerializeMsalV3());
		});
		return obj3;
	}

	private static async Task<AuthenticationResult> SilentAsync(IPublicClientApplication app, CancellationToken cancellationToken)
	{
		IAccount val = (await ((IClientApplicationBase)app).GetAccountsAsync().ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault() ?? throw new MsalUiRequiredException("no_account_for_login_hint", "no saved account");
		return await ((BaseAbstractAcquireTokenParameterBuilder<AcquireTokenSilentParameterBuilder>)(object)((IClientApplicationBase)app).AcquireTokenSilent((IEnumerable<string>)MicrosoftSignInRoute.Scopes, val)).ExecuteAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}
}
