using System.Collections.ObjectModel;

namespace Norcom.Admin.ViewModels;

public sealed class ModCard
{
    public required string Title { get; init; }

    public required string Group { get; init; }

    public required string Note { get; init; }

    public bool Found { get; init; }

    public ObservableCollection<SettingRow> Fields { get; } = new();
}
