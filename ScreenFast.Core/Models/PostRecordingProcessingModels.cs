namespace ScreenFast.Core.Models;

public enum PostRecordingStageKind
{
    RawFinalized,
    MetadataSidecar,
    ZoomPlan,
    StyledExport,
    History,
    PostRecordAction
}

public enum PostRecordingStageStatus
{
    Succeeded,
    Skipped,
    Failed
}

public enum RecordingProcessingState
{
    Success,
    PartialSuccess,
    Failure
}

public sealed record PostRecordingStageResult(
    PostRecordingStageKind Stage,
    PostRecordingStageStatus Status,
    string Message,
    string? ArtifactPath)
{
    public static PostRecordingStageResult Succeeded(PostRecordingStageKind stage, string message, string? artifactPath = null) =>
        new(stage, PostRecordingStageStatus.Succeeded, message, artifactPath);

    public static PostRecordingStageResult Skipped(PostRecordingStageKind stage, string message, string? artifactPath = null) =>
        new(stage, PostRecordingStageStatus.Skipped, message, artifactPath);

    public static PostRecordingStageResult Failed(PostRecordingStageKind stage, string message, string? artifactPath = null) =>
        new(stage, PostRecordingStageStatus.Failed, message, artifactPath);
}

public sealed record PostRecordingProcessingRequest(
    string SessionId,
    string FinalizedVideoPath,
    string FinalizedVideoFileName,
    DateTimeOffset RecordingStartedAtUtc,
    TimeSpan Duration,
    CaptureSourceModel? Source,
    RecordingSessionInfo? SessionInfo,
    bool IncludedSystemAudio,
    bool IncludedMicrophone,
    VideoQualityPreset QualityPreset,
    RecordingCountdownOption CountdownOption,
    PostRecordingOpenBehavior PostRecordingOpenBehavior,
    RecordingTelemetryTimeline TelemetryTimeline,
    IReadOnlyList<string> MetadataWarnings,
    ScreenFastPresetSelection PresetSelection,
    ScreenFastPresetLibrary PresetLibrary,
    ExportProfileLibrary ExportProfiles);

public sealed record PostRecordingProcessingResult(
    string RawVideoPath,
    RecordingAssetGraph AssetGraph,
    RecordingProcessingState State,
    IReadOnlyList<PostRecordingStageResult> Stages,
    IReadOnlyList<string> Warnings)
{
    public string BuildStatusMessage() => State switch
    {
        RecordingProcessingState.Success => $"Saved MP4 to {RawVideoPath}",
        RecordingProcessingState.PartialSuccess => $"Saved MP4 to {RawVideoPath}. Some post-record steps need attention.",
        _ => $"Saved MP4 to {RawVideoPath}. Post-record processing failed."
    };
}

public sealed record ZoomPlanArtifact(
    int SchemaVersion,
    string RecordingId,
    string RawVideoPath,
    string ZoomPresetId,
    string ZoomPresetName,
    bool HasTelemetry,
    IReadOnlyList<ZoomPlanSegment> Segments,
    IReadOnlyList<string> Warnings);

public sealed record ZoomPlanSegment(
    long StartMilliseconds,
    long EndMilliseconds,
    double Scale,
    double FocusX,
    double FocusY,
    string Reason);
