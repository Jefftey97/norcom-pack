using CommunityToolkit.Mvvm.ComponentModel;

namespace Norcom.Admin.ViewModels;

public partial class PropertyRow : ObservableObject
{
    public PropertyRow(string key, string value)
    {
        Key = key;
        this.value = value;
    }

    public string Key { get; }

    [ObservableProperty]
    private string value;
}
