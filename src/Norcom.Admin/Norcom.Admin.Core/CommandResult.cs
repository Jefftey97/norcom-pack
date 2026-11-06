namespace Norcom.Admin.Core;

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
