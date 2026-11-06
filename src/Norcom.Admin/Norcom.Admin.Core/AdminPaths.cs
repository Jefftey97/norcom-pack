namespace Norcom.Admin.Core;

public static class AdminPaths
{
    public static string Home => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NORCOM",
        "Admin");

    public static string SettingsPath => Path.Combine(Home, "settings.json");

    public static string LogPath => Path.Combine(Home, "admin.log");
}
