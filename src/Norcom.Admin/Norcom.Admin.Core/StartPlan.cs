namespace Norcom.Admin.Core;

public sealed record StartPlan(StartKind Kind, string FilePath, IReadOnlyList<string> Arguments, string Message);
