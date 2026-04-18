namespace ScreenFast.Core.Models;

public sealed record RecordingSourceMetadata(
    string SourceId,
    CaptureSourceKind SourceKind,
    string DisplayName,
    string Summary,
    int Width,
    int Height,
    SourceBoundsSnapshot? Bounds);

public sealed record RecordingSidecarMetadata(
    int SchemaVersion,
    string RecordingId,
    string SessionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset RecordingStartedAtUtc,
    string OutputVideoPath,
    string OutputVideoFileName,
    RecordingSourceMetadata Source,
    long DurationMilliseconds,
    VideoQualityPreset QualityPreset,
    string QualityPresetDisplayName,
    bool IncludedSystemAudio,
    bool IncludedMicrophone,
    RecordingCountdownOption CountdownOption,
    RecordingTelemetryTimeline Telemetry,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings);
