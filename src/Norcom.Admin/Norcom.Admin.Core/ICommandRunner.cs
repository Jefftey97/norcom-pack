namespace Norcom.Admin.Core;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken);
}
