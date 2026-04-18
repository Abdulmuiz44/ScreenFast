namespace ScreenFast.Core.Models;

public sealed record DiagnosticsMetadataSidecarSummary(
    string VideoFileName,
    string MetadataPath,
    bool Exists,
    long? MetadataFileSizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    int? SchemaVersion,
    int? CursorSampleCount,
    int? ClickEventCount,
    IReadOnlyList<string> Warnings);
