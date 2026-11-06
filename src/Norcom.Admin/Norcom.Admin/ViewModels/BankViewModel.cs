using System.Collections.ObjectModel;
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
        Ledger.Clear();
        var rows = BankLedger.ReadLast(40);
        if (rows.Count == 0)
        {
            LedgerNote = "No Admin bank ops yet";
        }
        else
        {
            LedgerNote = "Admin ops only. Server price history is not a bank statement.";
            foreach (var row in rows.Reverse())
            {
                var who = row.Target.Length == 0 ? row.Player : row.Player + " → " + row.Target;
                Ledger.Add(row.Time + "  " + row.Op + "  " + who + "  " + row.Amount + "  " + row.TxId);
            }
        }

        Status = snapshot.Note;
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
                Status = "Remove was accepted, but give failed. " + _redactor.Clean(second ?? "RCON did not answer.");
                _redactor.WriteLog("bank tx " + tx + " partial " + remove);
                return;
            }

            WriteLedger(tx, player, "transfer", amount, target, first + " | " + second);
            Refresh();
            Status = "Transfer sent. economy.json was re-read. tx " + tx;
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
            Refresh();
            Status = "Sent. economy.json was re-read. tx " + tx;
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
