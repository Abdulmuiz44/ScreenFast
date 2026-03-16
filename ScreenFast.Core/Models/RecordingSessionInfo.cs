namespace ScreenFast.Core.Models;

public sealed record RecordingSessionInfo(
    string FilePath,
    int Width,
    int Height,
    int FrameRate,
    bool IncludesSystemAudio,
    bool IncludesMicrophone,
    VideoQualityPreset QualityPreset,
    string? WarningMessage);
