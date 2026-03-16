namespace ScreenFast.Core.Models;

public sealed record AppSettings(
    int Version,
    string? OutputFolder,
    bool IncludeSystemAudio,
    bool IncludeMicrophone,
    VideoQualityPreset QualityPreset,
    HotkeySettings Hotkeys,
    bool LaunchMinimizedToTray,
    bool CloseToTray,
    bool MinimizeToTray,
    CaptureSourceModel? LastSelectedSource)
{
    public static AppSettings CreateDefault() =>
        new(
            1,
            null,
            false,
            false,
            VideoQualityPreset.Standard,
            HotkeySettings.CreateDefault(),
            false,
            true,
            true,
            null);
}
