using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingMetadataSidecarService : IRecordingMetadataSidecarService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IScreenFastLogService _logService;

    public RecordingMetadataSidecarService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public string GetSidecarPath(string videoFilePath)
    {
        return Path.ChangeExtension(videoFilePath, ".screenfast.json");
    }

    public async Task<OperationResult<string>> SaveAsync(RecordingSidecarMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(metadata.OutputVideoPath))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("Metadata cannot be saved without an output video path."));
        }

        var sidecarPath = GetSidecarPath(metadata.OutputVideoPath);
        var folder = Path.GetDirectoryName(sidecarPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("Metadata cannot be saved without an output folder."));
        }

        var tempPath = sidecarPath + ".tmp";
        try
        {
            Directory.CreateDirectory(folder);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, sidecarPath, true);
            _logService.Info(
                "metadata.sidecar_saved",
                "ScreenFast saved recording metadata sidecar.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = sidecarPath,
                    ["videoPath"] = metadata.OutputVideoPath,
                    ["cursorSampleCount"] = metadata.Telemetry.CursorSamples.Count,
                    ["clickEventCount"] = metadata.Telemetry.ClickEvents.Count
                });
            return OperationResult<string>.Success(sidecarPath);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }

            _logService.Warning(
                "metadata.sidecar_save_failed",
                "ScreenFast could not save recording metadata sidecar. The MP4 recording remains valid.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = sidecarPath,
                    ["videoPath"] = metadata.OutputVideoPath,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(AppError.ShellActionFailed($"ScreenFast could not save recording metadata: {ex.Message}"));
        }
    }
}
