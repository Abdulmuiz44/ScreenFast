namespace ScreenFast.Core.Models;

public sealed record RecordingPresetSnapshot(
    string RecordingPresetId,
    string RecordingPresetName,
    string ZoomPresetId,
    string ZoomPresetName,
    string StylingPresetId,
    string StylingPresetName,
    string ExportPresetId,
    string ExportPresetName,
    string ExportProfileId,
    string ExportProfileName);

public sealed record RecordingAssetGraph(
    string RawVideoPath,
    string? MetadataSidecarPath,
    string? ZoomPlanPath,
    string? StyledExportPath,
    RecordingPresetSnapshot Presets,
    RecordingProcessingState ProcessingState,
    IReadOnlyList<string> Warnings)
{
    public bool HasMetadataSidecar => !string.IsNullOrWhiteSpace(MetadataSidecarPath);
    public bool HasZoomPlan => !string.IsNullOrWhiteSpace(ZoomPlanPath);
    public bool HasStyledExport => !string.IsNullOrWhiteSpace(StyledExportPath);
}
