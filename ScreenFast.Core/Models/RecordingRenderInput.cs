namespace ScreenFast.Core.Models;

public sealed record RecordingRenderInput(
    string MetadataPath,
    RecordingSidecarMetadata Metadata)
{
    public string VideoPath => Metadata.OutputVideoPath;

    public RecordingTelemetryTimeline Telemetry => Metadata.Telemetry;

    public RecordingSourceMetadata Source => Metadata.Source;
}
