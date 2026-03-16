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
    VideoQualityPreset QualityPreset)
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
            VideoQualityPreset.Standard);
}
