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
    PostRecordingOpenBehavior PostRecordingOpenBehavior,
    bool IsOnboardingDismissed,
    string? DismissedRecoverySessionId,
    RecordingCountdownOption CountdownOption,
    bool OverlayEnabled,
    CaptureSourceModel? LastSelectedSource)
{
    public static AppSettings CreateDefault() =>
        new(
            2,
            null,
            false,
            false,
            VideoQualityPreset.Standard,
            HotkeySettings.CreateDefault(),
            false,
            true,
            true,
            PostRecordingOpenBehavior.None,
            false,
            null,
            RecordingCountdownOption.Off,
            true,
            null);
}
