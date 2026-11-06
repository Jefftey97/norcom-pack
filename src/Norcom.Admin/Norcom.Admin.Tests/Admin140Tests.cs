using Norcom.Admin.Core;
using Xunit;

namespace Norcom.Admin.Tests;

public class Admin140Tests
{
    private const string SparkText = """
        TPS from last 5s, 10s, 1m, 5m, 15m:
          20.0, 19.8, 19.5, 19.2, 18.9
        Tick durations (min/med/95%ile/max ms) from last 10s, 1m:
          4.1 / 12.0 / 28.4 / 41.2;  3.8 / 11.2 / 30.1 / 55.0
        CPU usage from last 10s, 1m, 15m:
          22.0%, 21.4%, 19.0% (system)
          8.1%, 7.4%, 6.2% (process)
        Memory usage:
          3.2 GB / 8.0 GB (40%)
        G1 Young Generation collector:
          8.21 ms avg, 42 total collections
          15s avg frequency
        G1 Old Generation collector:
          40.5 ms avg, 2 total collections
          12m 4s avg frequency
        Average Pings (min/med/95%ile/max ms) from now, last 15m:
          12 / 40 / 88 / 140;  10 / 36 / 90 / 150
        Entities: 842
        Loaded chunks: 1,204
        """;

    [Fact]
    public void SparkReport_reads_dashboard_fields()
    {
        var report = SparkReportParser.Parse(SparkText);
        Assert.Equal("19.8 / 19.5 / 19.2 / 18.9", report.TpsLine);
        Assert.Equal("3.8", report.MsptMin);
        Assert.Equal("11.2", report.MsptMedian);
        Assert.Equal("30.1", report.MsptP95);
        Assert.Equal("55.0", report.MsptMax);
        Assert.True(report.MsptAbove40);
        Assert.Equal("7.4", report.CpuProcess1m);
        Assert.Equal("21.4", report.CpuSystem1m);
        Assert.Equal("3.2 GB", report.HeapUsed);
        Assert.Equal("8.0 GB", report.HeapMax);
        Assert.Equal("42", report.Young?.Count);
        Assert.Equal("8.21 ms", report.Young?.AvgPause);
        Assert.Equal("15s", report.Young?.Frequency);
        Assert.Equal("2", report.Old?.Count);
        Assert.Equal("40", report.PingMedian);
        Assert.Equal("88", report.PingP95);
        Assert.Equal("842", report.Entities);
        Assert.Equal("1,204", report.Chunks);
    }

    [Fact]
    public void Backup_refuses_a_live_world_and_dedupes_the_maintenance_window()
    {
        var quiet = new DateTime(2026, 11, 6, 15, 0, 0);
        var window = new DateTime(2026, 11, 6, 4, 0, 30);
        var edge = new DateTime(2026, 11, 6, 4, 1, 59);
        var after = new DateTime(2026, 11, 6, 4, 2, 0);

        Assert.Equal(BackupStep.Zip, BackupPolicy.Decide(javaWriting: false, canFlush: true, duplicate: false));
        Assert.Equal(BackupStep.FlushThenRecheck, BackupPolicy.Decide(javaWriting: true, canFlush: true, duplicate: false));
        Assert.Equal(BackupStep.Refuse, BackupPolicy.Decide(javaWriting: true, canFlush: false, duplicate: false));
        Assert.Equal(BackupStep.Skip, BackupPolicy.Decide(javaWriting: true, canFlush: true, duplicate: true));
        Assert.Equal(BackupStep.Refuse, BackupPolicy.AfterFlush(stillWriting: true, duplicate: false));
        Assert.Equal(BackupStep.Zip, BackupPolicy.AfterFlush(stillWriting: false, duplicate: false));
        Assert.True(BackupPolicy.IsDuplicate(window, false, false));
        Assert.True(BackupPolicy.IsDuplicate(edge, false, false));
        Assert.True(BackupPolicy.IsDuplicate(quiet, freshAutoZip: true, maintenanceFlag: false));
        Assert.True(BackupPolicy.IsDuplicate(quiet, false, maintenanceFlag: true));
        Assert.False(BackupPolicy.IsDuplicate(after, false, false));
        Assert.False(BackupPolicy.IsDuplicate(quiet, false, false));
        Assert.Contains("nightly auto zip", BackupPolicy.FlushStillLiveMessage("session.lock"), StringComparison.Ordinal);
        Assert.Contains("nightly auto zip", BackupPolicy.NoFlushPathMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop", BackupPolicy.NoFlushPathMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Fresh_auto_zip_and_maintenance_flag_are_detected()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        var auto = Path.Combine(root, "backups", "auto");
        Directory.CreateDirectory(auto);
        var zip = Path.Combine(auto, "world-auto.zip");
        File.WriteAllText(zip, "zip");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(zip, now.AddMinutes(-1));
        Assert.True(BackupSignals.FreshAutoZip(root, now));
        File.SetLastWriteTimeUtc(zip, now.AddMinutes(-3));
        Assert.False(BackupSignals.FreshAutoZip(root, now));

        var flag = Path.Combine(root, "maintenance.flag");
        File.WriteAllText(flag, "maint");
        File.SetLastWriteTimeUtc(flag, now.AddSeconds(-30));
        Assert.True(BackupSignals.MaintenanceFlag(root, now));
        File.SetLastWriteTimeUtc(flag, now.AddHours(-20));
        Assert.False(BackupSignals.MaintenanceFlag(root, now));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Zip_is_not_created_while_session_lock_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world\n");
        File.WriteAllText(Path.Combine(root, "world", "level.dat"), "world");
        File.WriteAllText(Path.Combine(root, "world", "session.lock"), "lock");
        var ex = Assert.Throws<InvalidOperationException>(() => BackupArchive.CreateZip(root));
        Assert.Contains("nightly auto zip", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, "backups")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Quiet_world_zips_without_session_lock()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world\n");
        File.WriteAllText(Path.Combine(root, "world", "level.dat"), "world");
        File.WriteAllText(Path.Combine(root, "world", "session.lock"), "stale");
        File.Delete(Path.Combine(root, "world", "session.lock"));
        var zip = BackupArchive.CreateZip(root);
        Assert.True(File.Exists(zip));
        Assert.EndsWith(".zip", zip, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Swap_and_disk_baselines_are_health_critical()
    {
        long gb = 1024L * 1024 * 1024;
        Assert.True(HostHealth.SwapCritical(35 * gb, 48 * gb));
        Assert.False(HostHealth.SwapCritical(2 * gb, 48 * gb));
        Assert.True(HostHealth.DiskHigh(83));
        Assert.False(HostHealth.DiskHigh(79));
        Assert.Equal("disk high, swap critical", ServerHealth.Score(false, true, true, false));
    }

    [Fact]
    public void Meminfo_reads_ram_and_swap()
    {
        const string info = """
            MemTotal:       32768000 kB
            MemAvailable:   10485760 kB
            SwapTotal:      50331648 kB
            SwapFree:       13631488 kB
            """;
        var ram = HostSnapshot.MemoryFromMemInfo(info);
        var swap = HostSnapshot.SwapFromMemInfo(info);
        Assert.True(ram.Used > 0);
        Assert.True(HostHealth.SwapCritical(swap.Used, swap.Total));
        Assert.Contains("GB", swap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_reads_latest_log_while_the_file_is_open()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        var log = Path.Combine(root, "logs", "latest.log");
        File.WriteAllText(log, "start\nMSPT 12.5 tick\n");
        using var hold = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var overview = ServerOverviewReader.Read(root);
        Assert.Contains("MSPT 12.5", overview.TickLine, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Trend_keeps_the_latest_samples()
    {
        var trend = new TrendBuffer();
        for (var i = 0; i < 60; i++)
        {
            trend.Add(i);
        }

        Assert.Equal(TrendBuffer.MaxSamples, trend.Count);
        var strip = trend.Strip();
        Assert.Equal(TrendBuffer.MaxSamples, strip.IndexOf(' ', StringComparison.Ordinal));
        Assert.EndsWith("59.0", strip, StringComparison.Ordinal);
    }

    [Fact]
    public void Pin_changed_flag_reads_and_deletes_without_touching_the_pin()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-flag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".norcom"));
        var pin = Path.Combine(root, ".norcom", "server-pin.json");
        File.WriteAllText(pin, "{\"schema\":1}");
        File.WriteAllText(PinChangedFlag.PathFor(root), "07983921b11ec8e8443bb307817c7b10c90c4388491c45d05da2a819108936ac");
        Assert.True(PinChangedFlag.TryRead(root, out var value));
        Assert.StartsWith("07983921", value, StringComparison.Ordinal);
        PinChangedFlag.Delete(root);
        Assert.False(PinChangedFlag.TryRead(root, out _));
        Assert.Equal("{\"schema\":1}", File.ReadAllText(pin));
        Directory.Delete(root, true);
    }

    [Fact]
    public void Secrets_and_scheduler_policy_stay_closed()
    {
        var clean = new SecretRedactor().Clean("token=ghp_abcdefghijklmnopqrstuvwxyz123456");
        Assert.DoesNotContain("ghp_", clean, StringComparison.Ordinal);
        Assert.False(SchedulerPolicy.IsAllowed("GH push"));
        Assert.False(SchedulerPolicy.IsAllowed("silent publish"));
        Assert.True(SchedulerPolicy.IsAllowed("watch-flag"));
    }

    [Fact]
    public void Crash_inbox_names_the_last_fault()
    {
        Assert.Equal("Description: Watching server", CrashReports.FaultLine("----\nDescription: Watching server\n"));
    }

    [Fact]
    public void Config_list_follows_level_name_not_world()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "world", "serverconfig"));
        Directory.CreateDirectory(Path.Combine(root, "world_norcom", "serverconfig"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world_norcom\nmotd=Live NORCOM\n");
        File.WriteAllText(Path.Combine(root, "config", "pack.properties"), "ok=1");
        File.WriteAllText(Path.Combine(root, "world", "serverconfig", "stale.toml"), "x=1");
        File.WriteAllText(Path.Combine(root, "world_norcom", "serverconfig", "live.toml"), "x=2");
        var files = ServerCommands.ListConfigs(root);
        Assert.Contains(files, file => file.Replace('\\', '/').EndsWith("config/pack.properties", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => file.Replace('\\', '/').EndsWith("world_norcom/serverconfig/live.toml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, file => file.Replace('\\', '/').Contains("world/serverconfig", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("world_norcom", ServerPresence.LevelName(root));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Online_uses_level_name_lock_not_a_stale_pid()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world_norcom"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world_norcom\n");
        File.WriteAllText(Path.Combine(root, "norcom-admin.pid"), "999999");
        Assert.False(ServerPresence.IsOnline(root, false, Array.Empty<JavaSeen>()));
        File.WriteAllText(Path.Combine(root, "world_norcom", "session.lock"), "held");
        Assert.True(ServerPresence.SessionLockExists(root));
        Assert.True(ServerPresence.IsOnline(root, false, Array.Empty<JavaSeen>()));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Online_accepts_fabric_java_and_local_probe()
    {
        Assert.True(ServerPresence.CommandMatches(
            @"C:\Java\bin\java.exe -Xms8G -Xmx8G -jar G:\MinecraftServer\fabric-server-launch.jar nogui",
            "",
            @"G:\MinecraftServer"));
        Assert.True(ServerPresence.CommandMatches(
            "java.exe -jar fabric-server-launch.jar nogui",
            @"G:\MinecraftServer",
            @"G:\MinecraftServer"));
        Assert.False(ServerPresence.CommandMatches(
            @"java.exe -jar D:\other\fabric-server-launch.jar",
            @"D:\other",
            @"G:\MinecraftServer"));
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.False(ServerPresence.IsOnline(root, false, Array.Empty<JavaSeen>()));
        Assert.True(ServerPresence.IsOnline(root, true, Array.Empty<JavaSeen>()));
        Assert.Equal("world_norcom", ServerPresence.LevelName(root));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Bank_reads_wallets_and_refuses_a_bad_amount()
    {
        var root = Path.Combine(Path.GetTempPath(), "norcom-admin-test-" + Guid.NewGuid().ToString("N"));
        var eco = Path.Combine(root, "world_norcom", "data", "simpleeco");
        Directory.CreateDirectory(eco);
        Directory.CreateDirectory(Path.Combine(root, "config", "simpleeco"));
        File.WriteAllText(Path.Combine(root, "server.properties"), "level-name=world_norcom\n");
        File.WriteAllText(Path.Combine(root, "config", "simpleeco", "config.json"), "{\"currencyName\":\"NORCOM Coins\",\"currencySymbol\":\"C\",\"startingMoney\":50}");
        File.WriteAllText(Path.Combine(eco, "economy.json"), "{\"balances\":{\"aaa\":12},\"bankBalances\":{\"bbb\":4},\"playerNames\":{\"aaa\":\"Steve\",\"bbb\":\"Alex\"}}");
        var snapshot = NorcomBank.Read(root);
        Assert.Equal("NORCOM Coins", snapshot.CurrencyName);
        Assert.Equal("C", snapshot.CurrencySymbol);
        Assert.Equal(2, snapshot.Accounts.Count);
        Assert.Contains(snapshot.Accounts, account => account.Name == "Steve" && account.Wallet == 12 && account.Banked == 0);
        Assert.Contains(snapshot.Accounts, account => account.Name == "Alex" && account.Wallet == 0 && account.Banked == 4);
        Assert.False(NorcomBank.TryAmount("0", out _));
        Assert.False(NorcomBank.TryAmount("-3", out _));
        Assert.True(NorcomBank.TryAmount("50", out var amount));
        Assert.Equal(50, amount);
        Assert.Equal("eco set Steve 50", NorcomBank.SetCommand("Steve", 50));
        Assert.Equal("eco remove Steve 50", NorcomBank.RemoveCommand("Steve", 50));
        Assert.Equal("eco give Alex 50", NorcomBank.GiveCommand("Alex", 50));
        Assert.Contains("no console deposit", NorcomBank.BankMenuOnly, StringComparison.OrdinalIgnoreCase);
        var missing = BankerWalletSync.Run(root);
        Assert.False(missing.Ok);
        Assert.Contains("not found", missing.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kept", missing.Warning, StringComparison.OrdinalIgnoreCase);
        var ledger = Path.Combine(root, "bank-ledger.jsonl");
        BankLedger.Append(new BankLedgerRow("2026-11-06T06:00:00Z", "tx1", "Steve", "set", 50, "", "ok"), ledger);
        var serverLedger = Path.Combine(root, "admin", "bank-ledger.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(serverLedger)!);
        File.WriteAllText(serverLedger, "{\"txId\":\"shop1\",\"who\":\"Alex\",\"when\":\"2026-11-06T06:05:00Z\",\"kind\":\"shop\",\"amount\":-3,\"why\":\"npc debit\"}\n");
        var merged = BankLedger.ReadMerged(root, 10, ledger);
        Assert.Equal(2, merged.Count);
        Assert.Equal("shop1", merged[^1].TxId);
        Assert.Equal("Alex", merged[^1].Player);
        Assert.Equal("shop", merged[^1].Op);
        Assert.Equal(-3, merged[^1].Amount);
        Assert.Equal("npc debit", merged[^1].RconResult);
        File.WriteAllText(Path.Combine(root, "admin", "economy-ledger.jsonl"), "{\"ts\":\"2026-11-06T06:06:00Z\",\"tx\":\"NORCOM-TX-100-BREAD\",\"who\":\"Steve\",\"why\":\"stall buy\",\"amount\":-4,\"src\":\"shop\",\"last_gametime\":100}\n");
        var withShop = BankLedger.ReadMerged(root, 10, ledger);
        Assert.Contains(withShop, row => row.TxId == "NORCOM-TX-100-BREAD" && row.Player == "Steve" && row.Op == "shop" && row.Amount == -4);
        var prices = Path.Combine(root, "admin", "shop-prices.json");
        File.WriteAllText(prices, """
        {"stalls":[{"pos":"1 2 3","tag":"market","supplies":[{"sku":"BREAD","item":"minecraft:bread","qty":1,"price":4}],"buyback":[{"sku":"WHEAT","item":"minecraft:wheat","qty":8,"price":1}]}],"wandering_trader":{"interval_ticks":48000,"stay_ticks":4800,"mending":{"price":128,"emeralds":"64+64"},"books":[{"enchant":"sharpness","level":3,"price":32,"max_uses":1}]}}
        """);
        var desk = ShopPriceDesk.Load(root);
        Assert.Contains(desk.Offers, offer => offer.Side == "buy" && offer.Sku == "BREAD" && offer.Price == "4");
        Assert.Contains(desk.Offers, offer => offer.Side == "sell" && offer.Sku == "WHEAT");
        Assert.Contains(desk.Trader.Books, book => book.Enchant == "Mending" && book.Price == "128");
        File.WriteAllText(prices, """
        {"stalls":{"supplies":[{"sku":"BREAD","item":"minecraft:bread","qty":1,"price":4}],"buyback":[{"sku":"WHEAT","item":"minecraft:wheat","qty":8,"price":1}]},"trader":{"tag":"wanderer","buy":[{"sku":"GAPPLE","item":"minecraft:golden_apple","qty":1,"price":20,"shelf":"top"}],"sell":[{"sku":"IRON","item":"minecraft:iron_ingot","qty":4,"price":2,"shelf":"low"}]},"wandering_trader":{"interval_ticks":[54000,108000],"stay_ticks":[36000,48000],"books_price_emerald_or_coin_1to1":[{"sku":"BK01","enchantment":"sharpness","level":1,"price":8,"max_uses":3,"pay":"emerald"}],"mending":{"price":128,"inputs":"64+64","max_uses":1,"sku":"BK00-MENDING1","tier":"rare"}}}
        """);
        var live = ShopPriceDesk.Load(root);
        Assert.Equal(2, live.Offers.Count);
        Assert.DoesNotContain(live.Offers, offer => offer.Sku is "GAPPLE" or "IRON");
        Assert.Equal(2, live.Trader.Books.Count);
        Assert.Contains("1 commons + Mending (2 offers)", live.Trader.Note, StringComparison.Ordinal);
        Assert.Contains("54000", live.Trader.Schedule, StringComparison.Ordinal);
        Assert.Contains("108000", live.Trader.Schedule, StringComparison.Ordinal);
        Assert.Contains(live.Trader.Books, book => book.Sku == "BK00-MENDING1" && book.Pay == "64+64" && book.Tier == "rare" && book.MaxUses == "1");
        Assert.Contains(live.Trader.Books, book => book.Sku == "BK01" && book.Enchant == "sharpness");
        Assert.Contains(live.Trader.Shelves, shelf => shelf.Side == "buy" && shelf.Sku == "GAPPLE" && shelf.Shelf == "top");
        Assert.Contains(live.Trader.Shelves, shelf => shelf.Side == "sell" && shelf.Sku == "IRON");
        Assert.Contains("Server-owned", desk.Trader.Schedule, StringComparison.Ordinal);
        var bread = desk.Offers.First(offer => offer.Sku == "BREAD");
        Assert.Null(desk.Save(new Dictionary<string, string> { [bread.Pointer] = "9" }));
        var again = ShopPriceDesk.Load(root);
        Assert.Equal("9", again.Offers.First(offer => offer.Sku == "BREAD").Price);
        Assert.Equal("1", again.Offers.First(offer => offer.Sku == "WHEAT").Price);
        var rows = BankLedger.ReadLast(10, ledger);
        Assert.Single(rows);
        Assert.Equal("tx1", rows[0].TxId);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Settings_upsert_rejects_simulation_distance_and_passwords()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServerProperties.Upsert("simulation-distance=10\n", new Dictionary<string, string> { ["simulation-distance"] = "6" }));
        Assert.Throws<InvalidOperationException>(() =>
            ServerProperties.Upsert("", new Dictionary<string, string> { ["rcon.password"] = "secret" }));
    }
}
