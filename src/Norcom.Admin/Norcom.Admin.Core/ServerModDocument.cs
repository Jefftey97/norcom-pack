namespace Norcom.Admin.Core;

public sealed record ServerModDocument(string ModId, string Title, string Group, bool Found, string Note, IReadOnlyList<ServerSettingField> Fields);
