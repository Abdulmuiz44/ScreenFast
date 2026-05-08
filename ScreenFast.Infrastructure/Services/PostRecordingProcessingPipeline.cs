using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class PostRecordingProcessingPipeline : IPostRecordingProcessingPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRecordingMetadataSidecarService _metadataSidecarService;
    private readonly IRecordingHistoryService _historyService;
    private readonly IStyledExportService _styledExportService;
    private readonly IPostRecordingFileActionService _fileActionService;
    private readonly IScreenFastLogService _logService;

    public PostRecordingProcessingPipeline(
        IRecordingMetadataSidecarService metadataSidecarService,
        IRecordingHistoryService historyService,
        IStyledExportService styledExportService,
        IPostRecordingFileActionService fileActionService,
        IScreenFastLogService logService)
    {
        _metadataSidecarService = metadataSidecarService;
        _historyService = historyService;
        _styledExportService = styledExportService;
        _fileActionService = fileActionService;
        _logService = logService;
    }

    public async Task<PostRecordingProcessingResult> ProcessSuccessfulRecordingAsync(PostRecordingProcessingRequest request, CancellationToken cancellationToken = default)
    {
        var stages = new List<PostRecordingStageResult>
        {
            PostRecordingStageResult.Succeeded(PostRecordingStageKind.RawFinalized, "Raw MP4 finalized successfully.", request.FinalizedVideoPath)
        };
        var warnings = request.MetadataWarnings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        var metadataPath = await SaveMetadataAsync(request, stages, warnings, cancellationToken);
        var zoomPlanPath = await SaveZoomPlanAsync(request, metadataPath, stages, warnings, cancellationToken);
        var exportProfile = ResolveExportProfile(request);
        var styledExportPath = await TryStyledExportAsync(request, exportProfile, metadataPath, zoomPlanPath, stages, warnings, cancellationToken);
        await RunPostRecordActionAsync(request, stages, warnings, cancellationToken);

        var state = stages.Any(x => x.Status == PostRecordingStageStatus.Failed)
            ? RecordingProcessingState.PartialSuccess
            : RecordingProcessingState.Success;
        var assetGraph = BuildAssetGraph(request, exportProfile, metadataPath, zoomPlanPath, styledExportPath, warnings, stages)
            with
            {
                ProcessingState = state,
                Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
            };

        await AddHistoryAsync(request, assetGraph, stages, warnings, cancellationToken);
        state = stages.Any(x => x.Status == PostRecordingStageStatus.Failed)
            ? RecordingProcessingState.PartialSuccess
            : RecordingProcessingState.Success;
        assetGraph = assetGraph with { ProcessingState = state, Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray() };

        _logService.Info(
            "post_record.pipeline_completed",
            "ScreenFast completed post-record processing.",
            new Dictionary<string, object?>
            {
                ["rawVideoPath"] = request.FinalizedVideoPath,
                ["state"] = state,
                ["stageCount"] = stages.Count,
                ["warningCount"] = warnings.Count
            });

        return new PostRecordingProcessingResult(request.FinalizedVideoPath, assetGraph, state, stages, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<string?> SaveMetadataAsync(PostRecordingProcessingRequest request, List<PostRecordingStageResult> stages, List<string> warnings, CancellationToken cancellationToken)
    {
        if (request.SessionInfo is null || request.Source is null)
        {
            const string message = "Metadata sidecar skipped because recording context was incomplete.";
            stages.Add(PostRecordingStageResult.Skipped(PostRecordingStageKind.MetadataSidecar, message));
            warnings.Add(message);
            return null;
        }

        try
        {
            var metadata = BuildMetadata(request);
            var result = await _metadataSidecarService.SaveAsync(metadata, cancellationToken);
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
            {
                stages.Add(PostRecordingStageResult.Succeeded(PostRecordingStageKind.MetadataSidecar, "Metadata sidecar saved.", result.Value));
                return result.Value;
            }

            var message = result.Error?.Message ?? "Metadata sidecar could not be saved.";
            stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.MetadataSidecar, message));
            warnings.Add(message);
            return null;
        }
        catch (Exception ex)
        {
            var message = $"Metadata sidecar failed: {ex.Message}";
            stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.MetadataSidecar, message));
            warnings.Add(message);
            _logService.Warning("post_record.metadata_failed", "ScreenFast could not save metadata sidecar.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return null;
        }
    }

    private async Task<string?> SaveZoomPlanAsync(PostRecordingProcessingRequest request, string? metadataPath, List<PostRecordingStageResult> stages, List<string> warnings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadataPath))
        {
            const string message = "Zoom plan skipped because no metadata sidecar was available.";
            stages.Add(PostRecordingStageResult.Skipped(PostRecordingStageKind.ZoomPlan, message));
            warnings.Add(message);
            return null;
        }

        try
        {
            var zoomPreset = ResolveZoomPreset(request);
            var hasTelemetry = request.TelemetryTimeline.CursorSamples.Count > 0 || request.TelemetryTimeline.ClickEvents.Count > 0;
            var planWarnings = new List<string>();
            if (!hasTelemetry)
            {
                planWarnings.Add("Zoom plan contains a full-frame hold because cursor telemetry was unavailable.");
            }

            var plan = new ZoomPlanArtifact(
                1,
                request.SessionId,
                request.FinalizedVideoPath,
                zoomPreset.Id,
                zoomPreset.DisplayName,
                hasTelemetry,
                [new ZoomPlanSegment(0, Math.Max(0, (long)Math.Round(request.Duration.TotalMilliseconds)), hasTelemetry ? zoomPreset.TargetScale : 1.0, 0.5, 0.5, hasTelemetry ? "Initial telemetry-driven placeholder segment." : "Fallback full-frame segment.")],
                planWarnings);

            var path = Path.ChangeExtension(request.FinalizedVideoPath, ".zoomplan.json");
            await using (var stream = File.Create(path))
            {
                await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken);
            }

            warnings.AddRange(planWarnings);
            stages.Add(PostRecordingStageResult.Succeeded(PostRecordingStageKind.ZoomPlan, "Zoom plan saved for future renderer use.", path));
            return path;
        }
        catch (Exception ex)
        {
            var message = $"Zoom plan generation failed: {ex.Message}";
            stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.ZoomPlan, message));
            warnings.Add(message);
            _logService.Warning("post_record.zoom_plan_failed", "ScreenFast could not generate the zoom plan.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return null;
        }
    }

    private async Task<string?> TryStyledExportAsync(PostRecordingProcessingRequest request, ExportProfile exportProfile, string? metadataPath, string? zoomPlanPath, List<PostRecordingStageResult> stages, List<string> warnings, CancellationToken cancellationToken)
    {
        if (!exportProfile.RequestsStyledOutput)
        {
            stages.Add(PostRecordingStageResult.Skipped(PostRecordingStageKind.StyledExport, "Styled export skipped because the selected profile is raw-only."));
            return null;
        }

        var result = await _styledExportService.TryCreateStyledExportAsync(request.FinalizedVideoPath, metadataPath, zoomPlanPath, exportProfile, cancellationToken);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
        {
            stages.Add(PostRecordingStageResult.Succeeded(PostRecordingStageKind.StyledExport, "Styled export created.", result.Value));
            return result.Value;
        }

        if (result.IsSuccess)
        {
            var message = "Styled export skipped because the renderer is not implemented in this build.";
            stages.Add(PostRecordingStageResult.Skipped(PostRecordingStageKind.StyledExport, message));
            warnings.Add(message);
            return null;
        }

        var error = result.Error?.Message ?? "Styled export failed.";
        stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.StyledExport, error));
        warnings.Add(error);
        return null;
    }

    private async Task AddHistoryAsync(PostRecordingProcessingRequest request, RecordingAssetGraph assetGraph, List<PostRecordingStageResult> stages, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            long? size = null;
            var exists = false;
            var info = new FileInfo(request.FinalizedVideoPath);
            if (info.Exists)
            {
                exists = true;
                size = info.Length;
            }

            var entry = new RecordingHistoryEntry(
                Guid.NewGuid(),
                request.FinalizedVideoPath,
                request.FinalizedVideoFileName,
                DateTimeOffset.UtcNow,
                request.Duration,
                request.Source is null ? "No source" : $"{request.Source.TypeDisplayName}: {request.Source.DisplayName}",
                request.IncludedSystemAudio,
                request.IncludedMicrophone,
                VideoQualityPresets.Get(request.QualityPreset).DisplayName,
                true,
                null,
                size,
                exists,
                assetGraph,
                assetGraph.Presets.ExportProfileName,
                assetGraph.ProcessingState,
                assetGraph.Warnings);

            await _historyService.AddEntryAsync(entry, cancellationToken);
            stages.Add(PostRecordingStageResult.Succeeded(PostRecordingStageKind.History, "History asset graph saved."));
        }
        catch (Exception ex)
        {
            var message = $"History update failed: {ex.Message}";
            stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.History, message));
            warnings.Add(message);
        }
    }

    private async Task RunPostRecordActionAsync(PostRecordingProcessingRequest request, List<PostRecordingStageResult> stages, List<string> warnings, CancellationToken cancellationToken)
    {
        var result = await _fileActionService.RunAsync(request.PostRecordingOpenBehavior, request.FinalizedVideoPath, cancellationToken);
        if (request.PostRecordingOpenBehavior == PostRecordingOpenBehavior.None)
        {
            stages.Add(PostRecordingStageResult.Skipped(PostRecordingStageKind.PostRecordAction, "No post-record file action selected."));
            return;
        }

        if (result.IsSuccess)
        {
            stages.Add(PostRecordingStageResult.Succeeded(PostRecordingStageKind.PostRecordAction, "Post-record file action completed."));
            return;
        }

        var message = result.Error?.Message ?? "Post-record file action failed.";
        stages.Add(PostRecordingStageResult.Failed(PostRecordingStageKind.PostRecordAction, message));
        warnings.Add(message);
    }

    private RecordingSidecarMetadata BuildMetadata(PostRecordingProcessingRequest request)
    {
        var source = request.Source!;
        var sourceMetadata = new RecordingSourceMetadata(
            source.SourceId,
            source.Type,
            source.DisplayName,
            $"{source.TypeDisplayName}: {source.DisplayName}",
            request.SessionInfo?.Width ?? source.Width,
            request.SessionInfo?.Height ?? source.Height,
            request.TelemetryTimeline.SourceBounds);
        var warnings = request.MetadataWarnings.Concat(request.TelemetryTimeline.Warnings).Distinct(StringComparer.Ordinal).ToArray();

        return new RecordingSidecarMetadata(
            1,
            Guid.NewGuid().ToString("N"),
            request.SessionId,
            DateTimeOffset.UtcNow,
            request.RecordingStartedAtUtc,
            request.FinalizedVideoPath,
            request.FinalizedVideoFileName,
            sourceMetadata,
            Math.Max(0, (long)Math.Round(request.Duration.TotalMilliseconds)),
            request.QualityPreset,
            VideoQualityPresets.Get(request.QualityPreset).DisplayName,
            request.IncludedSystemAudio,
            request.IncludedMicrophone,
            request.CountdownOption,
            request.TelemetryTimeline,
            ["Raw recording remains the source of truth. This sidecar feeds post-record zoom planning and future styled export."],
            warnings);
    }

    private RecordingAssetGraph BuildAssetGraph(PostRecordingProcessingRequest request, ExportProfile exportProfile, string? metadataPath, string? zoomPlanPath, string? styledExportPath, IReadOnlyList<string> warnings, IReadOnlyList<PostRecordingStageResult> stages)
    {
        return new RecordingAssetGraph(
            request.FinalizedVideoPath,
            metadataPath,
            zoomPlanPath,
            styledExportPath,
            BuildPresetSnapshot(request, exportProfile),
            stages.Any(x => x.Status == PostRecordingStageStatus.Failed) ? RecordingProcessingState.PartialSuccess : RecordingProcessingState.Success,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private RecordingPresetSnapshot BuildPresetSnapshot(PostRecordingProcessingRequest request, ExportProfile exportProfile)
    {
        var recording = request.PresetLibrary.RecordingPresets.FirstOrDefault(x => x.Id == request.PresetSelection.RecordingPresetId) ?? request.PresetLibrary.RecordingPresets[0];
        var zoom = ResolveZoomPreset(request);
        var styling = request.PresetLibrary.StylingPresets.FirstOrDefault(x => x.Id == request.PresetSelection.StylingPresetId) ?? request.PresetLibrary.StylingPresets[0];
        var exportPreset = request.PresetLibrary.ExportPresets.FirstOrDefault(x => x.Id == request.PresetSelection.ExportPresetId) ?? request.PresetLibrary.ExportPresets[0];
        return new RecordingPresetSnapshot(recording.Id, recording.DisplayName, zoom.Id, zoom.DisplayName, styling.Id, styling.DisplayName, exportPreset.Id, exportPreset.DisplayName, exportProfile.Id, exportProfile.DisplayName);
    }

    private ExportProfile ResolveExportProfile(PostRecordingProcessingRequest request) =>
        request.ExportProfiles.Profiles.FirstOrDefault(x => x.Id == request.PresetSelection.ExportProfileId)
        ?? request.ExportProfiles.Profiles.FirstOrDefault()
        ?? ExportProfileLibrary.CreateDefault().Profiles[0];

    private ZoomPreset ResolveZoomPreset(PostRecordingProcessingRequest request) =>
        request.PresetLibrary.ZoomPresets.FirstOrDefault(x => x.Id == request.PresetSelection.ZoomPresetId)
        ?? request.PresetLibrary.ZoomPresets.FirstOrDefault()
        ?? ScreenFastPresetLibrary.CreateDefault().ZoomPresets[0];
}
