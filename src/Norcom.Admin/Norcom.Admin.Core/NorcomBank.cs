using System.Globalization;
using System.Text.Json;

namespace Norcom.Admin.Core;

public sealed record BankAccount(string Uuid, string Name, long Wallet, long Banked);

public sealed record BankLedgerRow(string Time, string TxId, string Player, string Op, long Amount, string Target, string RconResult);

public sealed record BankSnapshot(
    string CurrencyName,
    string CurrencySymbol,
    string EconomyPath,
    bool EconomyFound,
    IReadOnlyList<BankAccount> Accounts,
    string Note);

public static class NorcomBank
{
    public const string DefaultName = "NORCOM Coins";
    public const string DefaultSymbol = "C";

    public static string EconomyPath(string serverDirectory) =>
        Path.Combine(ServerPresence.WorldDirectory(serverDirectory), "data", "simpleeco", "economy.json");

    public static string ConfigPath(string serverDirectory) =>
        Path.Combine(serverDirectory, "config", "simpleeco", "config.json");

    public static BankSnapshot Read(string serverDirectory)
    {
        var (name, symbol) = ReadLabels(ConfigPath(serverDirectory));
        var path = EconomyPath(serverDirectory);
        if (!File.Exists(path))
        {
            return new BankSnapshot(name, symbol, path, false, Array.Empty<BankAccount>(), "economy.json was not found. Nothing was invented.");
        }

        try
        {
            using var document = JsonDocument.Parse(SharedFile.ReadText(path));
            var root = document.RootElement;
            var wallets = ReadLongMap(root, "balances");
            var banks = ReadLongMap(root, "bankBalances");
            var names = ReadStringMap(root, "playerNames");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in wallets.Keys) ids.Add(id);
            foreach (var id in banks.Keys) ids.Add(id);
            foreach (var id in names.Keys) ids.Add(id);
            var accounts = ids
                .Select(id => new BankAccount(
                    id,
                    names.TryGetValue(id, out var player) ? player : "",
                    wallets.TryGetValue(id, out var wallet) ? wallet : 0,
                    banks.TryGetValue(id, out var banked) ? banked : 0))
                .OrderBy(account => string.IsNullOrEmpty(account.Name) ? account.Uuid : account.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new BankSnapshot(name, symbol, path, true, accounts, accounts.Count + " accounts from economy.json.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new BankSnapshot(name, symbol, path, false, Array.Empty<BankAccount>(), "economy.json could not be read. Nothing was invented.");
        }
    }

    public static bool TryAmount(string? text, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        return amount > 0 && amount <= 1_000_000_000_000L;
    }

    public static bool SafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length is >= 1 and <= 16 &&
        name.Trim().All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    public static string SetCommand(string player, long amount) =>
        "eco set " + player.Trim() + " " + amount.ToString(CultureInfo.InvariantCulture);

    public static string RemoveCommand(string player, long amount) =>
        "eco remove " + player.Trim() + " " + amount.ToString(CultureInfo.InvariantCulture);

    public static string GiveCommand(string player, long amount) =>
        "eco give " + player.Trim() + " " + amount.ToString(CultureInfo.InvariantCulture);

    public const string BankMenuOnly =
        "Not sent. Simple Eco 2.8 moves bank money only from the player /eco bank menu. There is no console deposit or withdraw command.";

    public static bool RconFailed(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("incorrect", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("player-only", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("not enough", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Name, string Symbol) ReadLabels(string path)
    {
        if (!File.Exists(path))
        {
            return (DefaultName, DefaultSymbol);
        }

        try
        {
            using var document = JsonDocument.Parse(SharedFile.ReadText(path));
            var root = document.RootElement;
            var name = ReadString(root, "currencyName");
            var symbol = ReadString(root, "currencySymbol");
            return (name.Length == 0 ? DefaultName : name, symbol.Length == 0 ? DefaultSymbol : symbol);
        }
        catch
        {
            return (DefaultName, DefaultSymbol);
        }
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
    }

    private static Dictionary<string, long> ReadLongMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var table) || table.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var entry in table.EnumerateObject())
        {
            if (entry.Value.TryGetInt64(out var number))
            {
                map[entry.Name] = number;
            }
        }

        return map;
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var table) || table.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var entry in table.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                map[entry.Name] = entry.Value.GetString() ?? "";
            }
        }

        return map;
    }
}

public static class BankLedger
{
    public static string PathFor() => Path.Combine(AdminPaths.Home, "bank-ledger.jsonl");

    public static string ServerPath(string serverDirectory) =>
        Path.Combine(serverDirectory, "admin", "bank-ledger.jsonl");

    public static bool IsShop(BankLedgerRow row) =>
        row.TxId.StartsWith("NORCOM-TX-", StringComparison.OrdinalIgnoreCase) ||
        row.Op.Equals("shop", StringComparison.OrdinalIgnoreCase);

    public static void EnsureServerFile(string serverDirectory)
    {
        var path = ServerPath(serverDirectory);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, "");
        }
    }

    public static IReadOnlyList<BankLedgerRow> ReadMerged(string serverDirectory, int count, string? adminPath = null)
    {
        try
        {
            EnsureServerFile(serverDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var rows = new List<BankLedgerRow>();
        rows.AddRange(ReadAll(adminPath ?? PathFor()));
        rows.AddRange(ReadAll(ServerPath(serverDirectory)));
        rows.AddRange(ReadAll(Path.Combine(serverDirectory, "admin", "economy-ledger.jsonl")));
        return rows
            .OrderBy(row => row.Time, StringComparer.Ordinal)
            .TakeLast(Math.Max(1, count))
            .ToList();
    }

    public static void Append(BankLedgerRow row, string? path = null)
    {
        path ??= PathFor();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(new
        {
            time = row.Time,
            txId = row.TxId,
            player = row.Player,
            op = row.Op,
            amount = row.Amount,
            target = row.Target,
            rconResult = row.RconResult
        });
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public static IReadOnlyList<BankLedgerRow> ReadLast(int count, string? path = null)
    {
        path ??= PathFor();
        if (!File.Exists(path))
        {
            return Array.Empty<BankLedgerRow>();
        }

        try
        {
            var rows = new List<BankLedgerRow>();
            foreach (var line in SharedFile.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var tx = First(root, "txId", "tx-id", "tx", "id");
                    var who = First(root, "player", "who");
                    var kind = First(root, "op", "kind", "src");
                    if (tx.Length == 0 && who.Length == 0 && kind.Length == 0)
                    {
                        continue;
                    }

                    var why = First(root, "rconResult", "why", "reason");
                    var game = First(root, "last_gametime", "lastGametime");
                    if (game.Length > 0)
                    {
                        why = (why + " gametime " + game).Trim();
                    }

                    rows.Add(new BankLedgerRow(
                        First(root, "time", "when", "ts"),
                        tx,
                        who,
                        kind,
                        Long(root, "amount"),
                        First(root, "target"),
                        why));
                }
                catch (JsonException)
                {
                }
            }

            return rows.TakeLast(Math.Max(1, count)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<BankLedgerRow>();
        }
    }

    private static IReadOnlyList<BankLedgerRow> ReadAll(string path) => ReadLast(int.MaxValue, path);

    private static string First(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return "";
    }

    private static long Long(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed) ? parsed : 0;
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
