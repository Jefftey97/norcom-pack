using System.Collections.ObjectModel;
using System.IO;
using Norcom.Admin.Core;

namespace Norcom.Admin.ViewModels;

public partial class BankViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private readonly Func<string> _serverPath;
    private readonly Func<int> _rconPort;
    private readonly Func<string> _password;
    private readonly SecretRedactor _redactor;
    private readonly RconClient _rcon;
    private readonly HashSet<string> _txIds = new(StringComparer.OrdinalIgnoreCase);
    private ShopPriceDesk _shop = ShopPriceDesk.Load("");
    private readonly List<BankLedgerRow> _ledgerRows = new();
    private bool _busy;

    public BankViewModel(Func<string> serverPath, Func<int> rconPort, Func<string> password, SecretRedactor redactor, RconClient rcon)
    {
        _serverPath = serverPath;
        _rconPort = rconPort;
        _password = password;
        _redactor = redactor;
        _rcon = rcon;
    }

    public ObservableCollection<BankAccount> Accounts { get; } = new();

    public ObservableCollection<string> Ledger { get; } = new();

    public ObservableCollection<ShopPriceEdit> BuyOffers { get; } = new();

    public ObservableCollection<ShopPriceEdit> SellOffers { get; } = new();

    public ObservableCollection<string> TraderBooks { get; } = new();

    public ObservableCollection<string> TraderShelves { get; } = new();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string status = "NORCOM Bank reads economy.json. Money writes go through RCON on 127.0.0.1.";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string currencyName = NorcomBank.DefaultName;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string currencySymbol = NorcomBank.DefaultSymbol;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string heroName = "No player selected";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string heroWallet = "—";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string heroBank = "—";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string amountText = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string transferTo = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private BankAccount? selected;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string ledgerNote = "No Admin bank ops yet";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string shopNote = "Stall prices load from admin\\shop-prices.json.";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string shopShaShort = "—";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string shopShaFull = "";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string shopSync = "No shop sync file yet.";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string ledgerFilter = "All";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string traderSchedule = "Schedule is Server-owned. Admin shows this SoT only.";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string traderNote = "";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void Refresh()
    {
        var snapshot = NorcomBank.Read(_serverPath());
        CurrencyName = snapshot.CurrencyName;
        CurrencySymbol = snapshot.CurrencySymbol;
        Accounts.Clear();
        foreach (var account in snapshot.Accounts)
        {
            Accounts.Add(account);
        }

        if (Selected != null)
        {
            Selected = Accounts.FirstOrDefault(account => string.Equals(account.Uuid, Selected.Uuid, StringComparison.OrdinalIgnoreCase));
        }

        Selected ??= Accounts.FirstOrDefault();
        ApplyHero();
        _ledgerRows.Clear();
        _ledgerRows.AddRange(BankLedger.ReadMerged(_serverPath(), 80));
        PaintLedger();
        ShopSync = SyncPulse(_serverPath());

        LoadShop();
        Status = snapshot.Note;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SavePrices()
    {
        if (!Ask.Yes("Save stall prices to admin\\shop-prices.json?"))
        {
            Status = "Shop price save cancelled. The file was not changed.";
            return;
        }

        var prices = BuyOffers.Concat(SellOffers).ToDictionary(row => row.Pointer, row => row.Price);
        var error = _shop.Save(prices);
        if (error != null)
        {
            Status = error;
            return;
        }

        _redactor.WriteLog("Saved stall prices in admin\\shop-prices.json");
        LoadShop();
        Status = "Stall prices saved to admin\\shop-prices.json.";
    }

    private void LoadShop()
    {
        _shop = ShopPriceDesk.Load(_serverPath());
        ShopNote = _shop.Note;
        ShopShaFull = ShopPriceDesk.FileSha256(_shop.Path);
        ShopShaShort = ShopShaFull.Length >= 8 ? ShopShaFull[..8] : (ShopShaFull.Length == 0 ? "—" : ShopShaFull);
        BuyOffers.Clear();
        SellOffers.Clear();
        foreach (var offer in _shop.Offers)
        {
            var row = new ShopPriceEdit(offer.Side, offer.Pos, offer.Tag, offer.Sku, offer.Item, offer.Qty, offer.Pointer, offer.Price);
            if (offer.Side == "sell")
            {
                SellOffers.Add(row);
            }
            else
            {
                BuyOffers.Add(row);
            }
        }

        TraderSchedule = _shop.Trader.Schedule;
        TraderNote = _shop.Trader.Note;
        TraderBooks.Clear();
        foreach (var book in _shop.Trader.Books)
        {
            var sku = book.Sku.Length == 0 ? "" : book.Sku + "  ";
            var level = book.Level.Length == 0 ? "" : " " + book.Level;
            var uses = book.MaxUses.Length == 0 ? "" : "  max " + book.MaxUses;
            var pay = book.Pay.Length == 0 ? "" : "  " + book.Pay;
            var tier = book.Tier.Length == 0 ? "" : "  " + book.Tier;
            TraderBooks.Add(sku + book.Enchant + level + "  " + book.Price + " C" + pay + uses + tier);
        }

        TraderShelves.Clear();
        foreach (var shelf in _shop.Trader.Shelves)
        {
            TraderShelves.Add(shelf.Side + "  " + shelf.Sku + "  " + shelf.Item + "  qty " + shelf.Qty + "  " + shelf.Price + " C  " + shelf.Shelf);
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetLedgerFilter(string? filter)
    {
        LedgerFilter = filter is "Bank" or "Shop" ? filter : "All";
        PaintLedger();
    }

    private void PaintLedger()
    {
        var rows = _ledgerRows.Where(row => LedgerFilter switch
        {
            "Shop" => BankLedger.IsShop(row),
            "Bank" => !BankLedger.IsShop(row),
            _ => true
        }).ToList();
        Ledger.Clear();
        if (rows.Count == 0)
        {
            LedgerNote = LedgerFilter == "Shop"
                ? "No shop rows yet."
                : "No Admin bank ops yet. Server lines appear here when admin\\bank-ledger.jsonl has them.";
            return;
        }

        LedgerNote = "All / Bank / Shop. Shop rows use NORCOM-TX- or economy-ledger src. Price history is not a statement.";
        foreach (var row in rows.AsEnumerable().Reverse())
        {
            var who = row.Target.Length == 0 ? row.Player : row.Player + " → " + row.Target;
            var why = row.RconResult.Length == 0 ? "" : "  " + row.RconResult;
            Ledger.Add(row.Time + "  " + row.Op + "  " + who + "  " + row.Amount + why + "  " + row.TxId);
        }
    }

    private static string SyncPulse(string serverDirectory)
    {
        var parts = new List<string>();
        var ledger = Path.Combine(serverDirectory, "admin", "economy-ledger.jsonl");
        var state = Path.Combine(serverDirectory, "admin", "shop-tx-state.json");
        if (File.Exists(ledger))
        {
            parts.Add("economy-ledger.jsonl " + File.GetLastWriteTime(ledger).ToString("HH:mm") + " (" + Age(ledger) + ")");
        }

        if (File.Exists(state))
        {
            parts.Add("sync state file age " + Age(state));
        }

        return parts.Count == 0 ? "No shop sync file yet." : string.Join(" · ", parts);
    }

    private static string Age(string path)
    {
        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalHours >= 1 ? (int)age.TotalHours + "h" : (int)age.TotalMinutes + "m";
    }

    partial void OnSelectedChanged(BankAccount? value) => ApplyHero();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task Deposit() => RefuseBankMenu();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task Withdraw() => RefuseBankMenu();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task SetBalance() => RunWallet("set", NorcomBank.SetCommand);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task Transfer()
    {
        if (!Ready(out var player, out var amount))
        {
            return;
        }

        var target = (TransferTo ?? "").Trim();
        if (!NorcomBank.SafeName(target) || target.Equals(player, StringComparison.OrdinalIgnoreCase))
        {
            Status = "Transfer needs a different player name of letters, numbers, or underscores.";
            return;
        }

        var remove = NorcomBank.RemoveCommand(player, amount);
        var give = NorcomBank.GiveCommand(target, amount);
        if (!Ask.Yes("Transfer " + amount + " " + CurrencySymbol + " from " + player + " to " + target + "?\n\n" + remove + "\n" + give))
        {
            Status = "Transfer cancelled. Nothing was sent.";
            return;
        }

        var tx = NewTx();
        if (tx == null)
        {
            return;
        }

        _busy = true;
        try
        {
            var first = await SendAsync(remove);
            if (first == null || NorcomBank.RconFailed(first))
            {
                Status = "Transfer stopped. " + _redactor.Clean(first ?? "RCON did not answer.") + " Nothing was given.";
                return;
            }

            var second = await SendAsync(give);
            if (second == null || NorcomBank.RconFailed(second))
            {
                var partialSync = await SyncNoteAsync();
                Status = "Remove was accepted, but give failed. " + _redactor.Clean(second ?? "RCON did not answer.") + partialSync;
                _redactor.WriteLog("bank tx " + tx + " partial " + remove);
                return;
            }

            WriteLedger(tx, player, "transfer", amount, target, first + " | " + second);
            var transferSync = await SyncNoteAsync();
            Refresh();
            Status = "Transfer sent. economy.json was re-read. tx " + tx + transferSync;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunWallet(string op, Func<string, long, string> command)
    {
        if (!Ready(out var player, out var amount))
        {
            return;
        }

        var line = command(player, amount);
        if (!Ask.Yes(op + " " + amount + " " + CurrencySymbol + " for " + player + "?\n\n" + line))
        {
            Status = "Cancelled. Nothing was sent.";
            return;
        }

        var tx = NewTx();
        if (tx == null)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await SendAsync(line);
            if (result == null || NorcomBank.RconFailed(result))
            {
                Status = "Not applied. " + _redactor.Clean(result ?? "RCON did not answer.");
                return;
            }

            WriteLedger(tx, player, op, amount, "", result);
            var sync = await SyncNoteAsync();
            Refresh();
            Status = "Sent. economy.json was re-read. tx " + tx + sync;
        }
        finally
        {
            _busy = false;
        }
    }

    private Task RefuseBankMenu()
    {
        Status = NorcomBank.BankMenuOnly;
        return Task.CompletedTask;
    }

    private bool Ready(out string player, out long amount)
    {
        player = Selected?.Name.Trim() ?? "";
        amount = 0;
        if (_busy)
        {
            Status = "A bank command is already running.";
            return false;
        }

        if (!NorcomBank.SafeName(player))
        {
            Status = "Select a player who has a Minecraft name. A bare UUID is not sent.";
            return false;
        }

        if (!NorcomBank.TryAmount(AmountText, out amount))
        {
            Status = "Amount must be a whole number greater than 0.";
            return false;
        }

        if (_password().Length == 0)
        {
            Status = "RCON password is not loaded. Nothing was sent.";
            return false;
        }

        return true;
    }

    private string? NewTx()
    {
        var tx = Guid.NewGuid().ToString("N");
        if (!_txIds.Add(tx))
        {
            Status = "Duplicate tx ignored. Nothing was sent.";
            return null;
        }

        return tx;
    }

    private async Task<string> SyncNoteAsync()
    {
        var result = await Task.Run(() => BankerWalletSync.Run(_serverPath()));
        if (result.Output.Length > 0)
        {
            _redactor.WriteLog("banker-wallet-sync " + result.Output);
        }

        return result.Warning.Length == 0 ? "" : " " + result.Warning;
    }

    private async Task<string?> SendAsync(string command)
    {
        try
        {
            return await _rcon.CommandAsync("127.0.0.1", _rconPort(), _password(), command, CancellationToken.None);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private void WriteLedger(string tx, string player, string op, long amount, string target, string result)
    {
        var safe = _redactor.Clean(result).Replace('\r', ' ').Replace('\n', ' ');
        if (safe.Length > 240)
        {
            safe = safe[..240];
        }

        var row = new BankLedgerRow(DateTimeOffset.Now.ToString("o"), tx, player, op, amount, target, safe);
        BankLedger.Append(row);
        _redactor.WriteLog("bank tx " + tx + " " + op + " " + player + " " + amount);
    }

    private void ApplyHero()
    {
        if (Selected == null)
        {
            HeroName = "No player selected";
            HeroWallet = "—";
            HeroBank = "—";
            return;
        }

        HeroName = Selected.Name.Length == 0 ? Selected.Uuid : Selected.Name;
        HeroWallet = Selected.Wallet.ToString();
        HeroBank = Selected.Banked.ToString();
    }
}
