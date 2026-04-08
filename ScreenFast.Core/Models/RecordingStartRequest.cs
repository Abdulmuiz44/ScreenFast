namespace ScreenFast.Core.Models;

public sealed record RecordingStartRequest(
    CaptureSourceModel Source,
    string OutputFolder,
    string OutputFilePath,
    bool IncludeSystemAudio,
    bool IncludeMicrophone,
    VideoQualityPreset QualityPreset);
