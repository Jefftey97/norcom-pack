using CommunityToolkit.Mvvm.ComponentModel;

namespace Norcom.Admin.ViewModels;

public partial class MetricTile : ObservableObject
{
    public MetricTile(string label, string hint)
    {
        Label = label;
        Hint = hint;
    }

    public string Label { get; }

    [ObservableProperty]
    private string hint;

    [ObservableProperty]
    private string value = "—";

    [ObservableProperty]
    private bool alert;
}
