using CommunityToolkit.Mvvm.ComponentModel;

namespace Norcom.Admin.ViewModels;

public partial class SettingRow : ObservableObject
{
    public required string File { get; init; }

    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Hint { get; init; }

    public bool IsBool { get; init; }

    public bool IsText => !IsBool;

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private bool flag;
}
