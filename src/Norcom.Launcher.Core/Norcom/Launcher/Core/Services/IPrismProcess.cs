using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public interface IPrismProcess
{
	Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface ITrackedPrismProcess : IPrismProcess
{
	int? ProcessId { get; }
}
