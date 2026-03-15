namespace ScreenFast.Core.Models;

public sealed record RecordingStartRequest(
    CaptureSourceModel Source,
    string OutputFolder,
    bool IncludeSystemAudio,
    bool IncludeMicrophone);
