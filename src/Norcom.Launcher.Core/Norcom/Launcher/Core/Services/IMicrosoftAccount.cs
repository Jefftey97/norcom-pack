using System;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public interface IMicrosoftAccount
{
	Task<MicrosoftAccountResult> AcquireAsync(string clientId, MicrosoftPrompt prompt, Action<DeviceCodePrompt>? deviceCode, CancellationToken cancellationToken);

	Task SignOutAsync(string clientId, CancellationToken cancellationToken);
}
