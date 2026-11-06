namespace Norcom.Admin.Core;

public sealed record ServerSettingField(string FileRelativeToServer, string Key, string Label, string Hint, string Value, string Kind);
