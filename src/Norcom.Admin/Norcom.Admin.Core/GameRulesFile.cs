namespace Norcom.Admin.Core;

public static class GameRulesFile
{
    public static readonly string[] Keys =
    [
        "keepInventory",
        "mobGriefing",
        "doFireTick",
        "doWeatherCycle",
        "doDaylightCycle",
        "spawnRadius"
    ];

    public const string RelativePath = "config/norcom-gamerules.properties";

    public static void Save(string serverDirectory, IReadOnlyDictionary<string, string> values)
    {
        var path = Path.Combine(serverDirectory, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var updates = values
            .Where(pair => Keys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, ServerProperties.Upsert(existing, updates));
    }
}
