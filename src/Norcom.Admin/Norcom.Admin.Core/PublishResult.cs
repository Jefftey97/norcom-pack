namespace Norcom.Admin.Core;

public sealed record PublishResult(bool Ok, bool Pushed, string Message, bool ClearFlag = false);
