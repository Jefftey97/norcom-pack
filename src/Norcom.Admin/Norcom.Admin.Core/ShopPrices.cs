using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norcom.Admin.Core;

public sealed record ShopOffer(
    string Side,
    string Pos,
    string Tag,
    string Sku,
    string Item,
    string Qty,
    string Pointer,
    string Price);

public sealed record TraderBook(string Enchant, string Level, string Price, string MaxUses, string Note);

public sealed record TraderCard(string Schedule, IReadOnlyList<TraderBook> Books, string Note);

public sealed class ShopPriceDesk
{
    private readonly JsonNode? _root;
    private readonly Dictionary<string, (JsonObject Owner, string Key, JsonValueKind Kind)> _prices = new();

    public string Path { get; }

    public string Note { get; }

    public IReadOnlyList<ShopOffer> Offers { get; }

    public TraderCard Trader { get; }

    private ShopPriceDesk(
        string path,
        string note,
        JsonNode? root,
        IReadOnlyList<ShopOffer> offers,
        TraderCard trader,
        Dictionary<string, (JsonObject Owner, string Key, JsonValueKind Kind)>? prices = null)
    {
        Path = path;
        Note = note;
        _root = root;
        Offers = offers;
        Trader = trader;
        if (prices != null)
        {
            foreach (var pair in prices)
            {
                _prices[pair.Key] = pair.Value;
            }
        }
    }

    public static string FilePath(string serverDirectory) =>
        System.IO.Path.Combine(serverDirectory, "admin", "shop-prices.json");

    public static ShopPriceDesk Load(string serverDirectory)
    {
        var path = FilePath(serverDirectory);
        var emptyTrader = new TraderCard("Schedule is Server-owned. Admin shows this SoT only.", Array.Empty<TraderBook>(), "No wandering trader block in shop-prices.json.");
        if (!File.Exists(path))
        {
            return new ShopPriceDesk(path, "shop-prices.json was not found. Nothing was invented.", null, Array.Empty<ShopOffer>(), emptyTrader);
        }

        try
        {
            var root = JsonNode.Parse(SharedFile.ReadText(path));
            if (root == null)
            {
                return new ShopPriceDesk(path, "shop-prices.json was empty. Nothing was invented.", null, Array.Empty<ShopOffer>(), emptyTrader);
            }

            var offers = new List<ShopOffer>();
            var prices = new Dictionary<string, (JsonObject Owner, string Key, JsonValueKind Kind)>();
            TraderCard? trader = null;
            Walk(root, "", new WalkCtx("", "", ""), offers, prices, ref trader);
            return new ShopPriceDesk(path, offers.Count + " stall prices from shop-prices.json.", root, offers, trader ?? emptyTrader, prices);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ShopPriceDesk(path, "shop-prices.json could not be read. Nothing was invented.", null, Array.Empty<ShopOffer>(), emptyTrader);
        }
    }

    public string? Save(IReadOnlyDictionary<string, string> pricesByPointer)
    {
        if (_root == null)
        {
            return "shop-prices.json was not loaded. Nothing was written.";
        }

        foreach (var pair in pricesByPointer)
        {
            if (!_prices.TryGetValue(pair.Key, out var slot))
            {
                return "A price row is no longer in the file. Refresh, then save again.";
            }

            if (!long.TryParse(pair.Value.Trim(), out var price) || price < 0)
            {
                return "Each price must be a whole number, 0 or more. Nothing was written.";
            }

            slot.Owner[slot.Key] = slot.Kind == JsonValueKind.String
                ? JsonValue.Create(price.ToString())
                : JsonValue.Create(price);
        }

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = Path + ".norcom-tmp";
        File.WriteAllText(temp, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, overwrite: true);
        return null;
    }

    private static void Walk(
        JsonNode? node,
        string pointer,
        WalkCtx ctx,
        List<ShopOffer> offers,
        Dictionary<string, (JsonObject, string, JsonValueKind)> prices,
        ref TraderCard? trader)
    {
        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                Walk(array[i], pointer + "/" + i, ctx, offers, prices, ref trader);
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        var next = ctx with
        {
            Pos = Prefer(Text(obj, "pos", "position"), ctx.Pos),
            Tag = Prefer(Text(obj, "tag"), ctx.Tag)
        };
        var priceKey = PriceKey(obj);
        var sku = Text(obj, "sku", "SKU");
        var item = Text(obj, "item", "itemId");
        if (priceKey != null && (sku.Length > 0 || item.Length > 0) && !ctx.InTrader)
        {
            var pricePointer = pointer + "/" + priceKey;
            var kind = obj[priceKey]?.GetValueKind() ?? JsonValueKind.Number;
            prices[pricePointer] = (obj, priceKey, kind);
            offers.Add(new ShopOffer(
                next.Side.Length == 0 ? "buy" : next.Side,
                next.Pos,
                next.Tag,
                sku.Length == 0 ? item : sku,
                item,
                Text(obj, "qty", "quantity", "count"),
                pricePointer,
                Text(obj, priceKey)));
        }

        foreach (var property in obj.ToList())
        {
            if (property.Key.Equals("wandering_trader", StringComparison.OrdinalIgnoreCase))
            {
                trader = ReadTrader(property.Value as JsonObject);
                continue;
            }

            var side = next.Side;
            if (IsBuyList(property.Key))
            {
                side = "buy";
            }
            else if (IsSellList(property.Key))
            {
                side = "sell";
            }

            Walk(property.Value, pointer + "/" + property.Key, next with { Side = side }, offers, prices, ref trader);
        }
    }

    private static TraderCard ReadTrader(JsonObject? trader)
    {
        var scheduleNote = "Schedule is Server-owned. Admin shows this SoT only.";
        if (trader == null)
        {
            return new TraderCard(scheduleNote, Array.Empty<TraderBook>(), "No wandering trader block.");
        }

        var interval = Text(trader, "interval_ticks", "intervalTicks", "interval");
        var stay = Text(trader, "stay_ticks", "stayTicks", "stay");
        var schedule = "Interval " + (interval.Length == 0 ? "—" : interval) + " ticks. Stay " + (stay.Length == 0 ? "—" : stay) + " ticks. " + scheduleNote;
        var books = new List<TraderBook>();
        if (trader["books"] is JsonArray list)
        {
            foreach (var entry in list)
            {
                if (entry is not JsonObject book)
                {
                    continue;
                }

                books.Add(new TraderBook(
                    Text(book, "enchant", "enchantment", "name"),
                    Text(book, "level"),
                    Text(book, "price"),
                    Text(book, "max_uses", "maxUses"),
                    Text(book, "note", "emeralds", "pay")));
            }
        }

        if (trader["mending"] is JsonObject mending)
        {
            books.Insert(0, new TraderBook("Mending", Text(mending, "level"), Text(mending, "price"), Text(mending, "max_uses", "maxUses"), Prefer(Text(mending, "note", "emeralds", "pay"), "64+64")));
        }

        return new TraderCard(schedule, books, books.Count + " trader books from shop-prices.json.");
    }

    private static string? PriceKey(JsonObject obj)
    {
        foreach (var key in new[] { "price", "price_c", "priceC" })
        {
            if (obj.ContainsKey(key))
            {
                return key;
            }
        }

        return null;
    }

    private static bool IsBuyList(string key) =>
        key.Equals("supplies", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("buy", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("buys", StringComparison.OrdinalIgnoreCase);

    private static bool IsSellList(string key) =>
        key.Equals("buyback", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("sell", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("sells", StringComparison.OrdinalIgnoreCase);

    private static string Text(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetPropertyValue(name, out var value) || value == null)
            {
                continue;
            }

            if (value is JsonValue json)
            {
                if (json.TryGetValue<string>(out var text))
                {
                    return text.Trim();
                }

                return json.ToJsonString().Trim('"');
            }
        }

        return "";
    }

    private static string Prefer(string next, string fallback) => next.Length == 0 ? fallback : next;

    private readonly record struct WalkCtx(string Side, string Pos, string Tag)
    {
        public bool InTrader { get; init; }
    }
}
