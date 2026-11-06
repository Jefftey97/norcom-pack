using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public sealed class PrismProcess : IPrismProcess
{
	public async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false
		};
		foreach (string argument in arguments)
		{
			processStartInfo.ArgumentList.Add(argument);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Windows did not start Prism.");
		await process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		return process.ExitCode;
	}
}
