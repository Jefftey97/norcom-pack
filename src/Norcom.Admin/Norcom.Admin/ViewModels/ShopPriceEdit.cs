using CommunityToolkit.Mvvm.ComponentModel;

namespace Norcom.Admin.ViewModels;

public partial class ShopPriceEdit : ObservableObject
{
    public ShopPriceEdit(string side, string pos, string tag, string sku, string item, string qty, string pointer, string price)
    {
        Side = side;
        Pos = pos;
        Tag = tag;
        Sku = sku;
        Item = item;
        Qty = qty;
        Pointer = pointer;
        Price = price;
    }

    public string Side { get; }

    public string Pos { get; }

    public string Tag { get; }

    public string Sku { get; }

    public string Item { get; }

    public string Qty { get; }

    public string Pointer { get; }

    [ObservableProperty]
    private string price;
}
