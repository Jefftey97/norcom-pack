using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class PrismPlay
{
	public static PrismPreparation Prepare(string? prismEnvironment, IEnumerable<string> prismCandidates, string prismDataDir, string gameDir, string javaPath, int ramMb)
	{
		string text = PrismInstall.Find(prismEnvironment, prismCandidates);
		if (text == null)
		{
			return new PrismPreparation
			{
				CanStart = false,
				Message = "Prism Launcher is not installed. Install it from the official Prism site, then press Play. Sign in with Microsoft in Prism's window if it asks. NORCOM does not start Minecraft without that."
			};
		}
		PrismInstance.Ensure(prismDataDir, gameDir, javaPath, ramMb);
		FriendsEndpoint endpoint = FriendsServer.Resolve();
		IReadOnlyList<string> arguments = PrismCommand.Build(endpoint.Host, endpoint.Port);
		return new PrismPreparation
		{
			CanStart = true,
			Message = "Opening Prism for the NORCOM instance. If Prism asks, sign in with Microsoft in that window. NORCOM does not keep that login and did not start an offline game.",
			Executable = text,
			Arguments = arguments
		};
	}

	public static async Task<PrismPreparation> RunAsync(PrismPreparation prepared, IPrismProcess process, CancellationToken cancellationToken)
	{
		if (!prepared.CanStart || prepared.Executable == null)
		{
			return prepared;
		}
		if (await process.RunAsync(prepared.Executable, prepared.Arguments, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) != 0)
		{
			return new PrismPreparation
			{
				CanStart = false,
				Message = "Prism did not start Minecraft. Finish Microsoft sign-in in Prism's window if it asked, then press Play again. NORCOM did not use a local name.",
				Executable = prepared.Executable,
				Arguments = prepared.Arguments
			};
		}
		return new PrismPreparation
		{
			CanStart = true,
			Message = "Prism closed. If you finished its Microsoft sign-in, it started the NORCOM instance. Later Play uses the same Prism. NORCOM does not keep that login.",
			Executable = prepared.Executable,
			Arguments = prepared.Arguments
		};
	}
}
