namespace ScreenFast.Core.Models;

public sealed record RecordingHistoryEntry(
    Guid Id,
    string FilePath,
    string FileName,
    DateTimeOffset CreatedAt,
    TimeSpan Duration,
    string SourceSummary,
    bool IncludedSystemAudio,
    bool IncludedMicrophone,
    string QualityPreset,
    bool IsSuccess,
    string? FailureSummary,
    long? FileSizeBytes,
    bool IsFileAvailable);
