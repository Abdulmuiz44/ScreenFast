using ScreenFast.Core.State;

namespace ScreenFast.Core.Models;

public sealed record RecorderStatusSnapshot(
    RecorderState State,
    string StatusMessage,
    string TimerText,
    CaptureSourceModel? SelectedSource,
    string? OutputFolder,
    bool IncludeSystemAudio,
    bool IncludeMicrophone,
    VideoQualityPreset QualityPreset,
    PostRecordingOpenBehavior PostRecordingOpenBehavior,
    RecordingCountdownOption CountdownOption,
    bool OverlayEnabled,
    int CountdownRemainingSeconds,
    string? PreflightWarningMessage)
{
    public static RecorderStatusSnapshot CreateDefault() =>
        new(
            RecorderState.Idle,
            "Choose a display or window to get ready.",
            "00:00:00",
            null,
            null,
            false,
            false,
            VideoQualityPreset.Standard,
            PostRecordingOpenBehavior.None,
            RecordingCountdownOption.Off,
            true,
            0,
            null);
}
