namespace ScreenFast.Core.Models;

public sealed record RecoverySessionMarker(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    string SourceSummary,
    string? TargetFilePath,
    VideoQualityPreset QualityPreset,
    bool IncludeSystemAudio,
    bool IncludeMicrophone);
