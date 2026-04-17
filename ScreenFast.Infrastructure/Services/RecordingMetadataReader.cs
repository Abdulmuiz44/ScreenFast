using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingMetadataReader : IRecordingMetadataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRecordingMetadataSidecarService _sidecarService;
    private readonly IScreenFastLogService _logService;

    public RecordingMetadataReader(
        IRecordingMetadataSidecarService sidecarService,
        IScreenFastLogService logService)
    {
        _sidecarService = sidecarService;
        _logService = logService;
    }

    public async Task<OperationResult<RecordingRenderInput>> ReadAsync(string metadataPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
        {
            return OperationResult<RecordingRenderInput>.Failure(AppError.SourceUnavailable("Recording metadata sidecar was not found."));
        }

        try
        {
            await using var stream = File.OpenRead(metadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<RecordingSidecarMetadata>(stream, JsonOptions, cancellationToken);
            if (metadata is null)
            {
                return OperationResult<RecordingRenderInput>.Failure(AppError.SourceUnavailable("Recording metadata sidecar was empty or invalid."));
            }

            return OperationResult<RecordingRenderInput>.Success(new RecordingRenderInput(metadataPath, metadata));
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "metadata.sidecar_read_failed",
                "ScreenFast could not read recording metadata sidecar.",
                new Dictionary<string, object?> { ["metadataPath"] = metadataPath, ["error"] = ex.Message });
            return OperationResult<RecordingRenderInput>.Failure(AppError.SourceUnavailable($"Recording metadata could not be read: {ex.Message}"));
        }
    }

    public async Task<DiagnosticsMetadataSidecarSummary> CreateDiagnosticsSummaryAsync(string videoFilePath, CancellationToken cancellationToken = default)
    {
        var metadataPath = _sidecarService.GetSidecarPath(videoFilePath);
        if (!File.Exists(metadataPath))
        {
            return new DiagnosticsMetadataSidecarSummary(
                Path.GetFileName(videoFilePath),
                metadataPath,
                false,
                null,
                null,
                null,
                null,
                null,
                []);
        }

        try
        {
            var info = new FileInfo(metadataPath);
            var readResult = await ReadAsync(metadataPath, cancellationToken);
            if (!readResult.IsSuccess || readResult.Value is null)
            {
                return new DiagnosticsMetadataSidecarSummary(
                    Path.GetFileName(videoFilePath),
                    metadataPath,
                    true,
                    info.Length,
                    info.LastWriteTimeUtc,
                    null,
                    null,
                    null,
                    [readResult.Error?.Message ?? "Metadata sidecar could not be read."]);
            }

            var metadata = readResult.Value.Metadata;
            return new DiagnosticsMetadataSidecarSummary(
                Path.GetFileName(videoFilePath),
                metadataPath,
                true,
                info.Length,
                info.LastWriteTimeUtc,
                metadata.SchemaVersion,
                metadata.Telemetry.CursorSamples.Count,
                metadata.Telemetry.ClickEvents.Count,
                metadata.Warnings.Concat(metadata.Telemetry.Warnings).Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "metadata.diagnostics_summary_failed",
                "ScreenFast could not summarize recording metadata for diagnostics.",
                new Dictionary<string, object?> { ["metadataPath"] = metadataPath, ["error"] = ex.Message });
            return new DiagnosticsMetadataSidecarSummary(
                Path.GetFileName(videoFilePath),
                metadataPath,
                true,
                null,
                null,
                null,
                null,
                null,
                [$"Metadata diagnostics summary failed: {ex.Message}"]);
        }
    }
}
