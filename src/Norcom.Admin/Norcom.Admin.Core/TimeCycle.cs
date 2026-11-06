namespace Norcom.Admin.Core;

public static class TimeCycle
{
    public const string RelativePath = "config/norcom-time.properties";

    public static string Format(int dayTicks, int nightTicks)
    {
        dayTicks = Math.Clamp(dayTicks, 1000, 72000);
        nightTicks = Math.Clamp(nightTicks, 1000, 72000);
        return "dayTicks=" + dayTicks + "\nnightTicks=" + nightTicks + "\n";
    }

    public static (int Day, int Night) Parse(string? text)
    {
        var map = ServerProperties.Read(text);
        var day = map.TryGetValue("dayTicks", out var dayText) && int.TryParse(dayText, out var dayValue) ? dayValue : 12000;
        var night = map.TryGetValue("nightTicks", out var nightText) && int.TryParse(nightText, out var nightValue) ? nightValue : 12000;
        return (Math.Clamp(day, 1000, 72000), Math.Clamp(night, 1000, 72000));
    }

    public static void Save(string serverDirectory, int dayTicks, int nightTicks)
    {
        var path = Path.Combine(serverDirectory, "config", "norcom-time.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Format(dayTicks, nightTicks));
    }
}
