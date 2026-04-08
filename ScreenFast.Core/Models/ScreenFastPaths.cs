namespace ScreenFast.Core.Models;

public static class ScreenFastPaths
{
    public static string RootFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenFast");

    public static string LogsFolderPath => Path.Combine(RootFolderPath, "Logs");
    public static string SettingsFilePath => Path.Combine(RootFolderPath, "settings.json");
    public static string HistoryFilePath => Path.Combine(RootFolderPath, "recording-history.json");
    public static string RecoveryStateFilePath => Path.Combine(RootFolderPath, "active-session.json");
}
