using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class StyledExportPlanService : IStyledExportPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRecordingMetadataReader _metadataReader;
    private readonly IStyledExportPlanner _planner;
    private readonly IScreenFastLogService _logService;

    public StyledExportPlanService(
        IRecordingMetadataReader metadataReader,
        IStyledExportPlanner planner,
        IScreenFastLogService logService)
    {
        _metadataReader = metadataReader;
        _planner = planner;
        _logService = logService;
    }

    public async Task<OperationResult<StyledExportPlan>> PlanFromArtifactsAsync(
        string metadataPath,
        string zoomPlanPath,
        StyledExportCompositionSettings? composition = null,
        string? suggestedOutputVideoPath = null,
        CancellationToken cancellationToken = default)
    {
        var renderInputResult = await _metadataReader.ReadAsync(metadataPath, cancellationToken);
        if (!renderInputResult.IsSuccess || renderInputResult.Value is null)
        {
            return OperationResult<StyledExportPlan>.Failure(renderInputResult.Error!);
        }

        var zoomPlanResult = await ReadZoomPlanAsync(zoomPlanPath, cancellationToken);
        if (!zoomPlanResult.IsSuccess || zoomPlanResult.Value is null)
        {
            return OperationResult<StyledExportPlan>.Failure(zoomPlanResult.Error!);
        }

        var planResult = _planner.Plan(
            renderInputResult.Value,
            zoomPlanResult.Value,
            composition ?? StyledExportCompositionSettings.Create(),
            zoomPlanPath,
            suggestedOutputVideoPath);

        if (!planResult.IsSuccess || planResult.Value is null)
        {
            _logService.Warning(
                "styled_export.plan_failed",
                "ScreenFast could not create a styled export plan.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = metadataPath,
                    ["zoomPlanPath"] = zoomPlanPath,
                    ["error"] = planResult.Error?.Message
                });
            return planResult;
        }

        _logService.Info(
            "styled_export.plan_created",
            "ScreenFast created a styled export plan.",
            new Dictionary<string, object?>
            {
                ["metadataPath"] = metadataPath,
                ["zoomPlanPath"] = zoomPlanPath,
                ["segmentCount"] = planResult.Value.Diagnostics.SegmentCount,
                ["suggestedOutputVideoPath"] = planResult.Value.SuggestedOutputVideoPath
            });
        return planResult;
    }

    public string GetStyledExportPlanPath(string metadataPath)
    {
        if (metadataPath.EndsWith(".screenfast.json", StringComparison.OrdinalIgnoreCase))
        {
            return metadataPath[..^".screenfast.json".Length] + ".styled-export.json";
        }

        return Path.ChangeExtension(metadataPath, ".styled-export.json");
    }

    public async Task<OperationResult<string>> SavePlanAsync(
        StyledExportPlan plan,
        string metadataPath,
        CancellationToken cancellationToken = default)
    {
        var planPath = GetStyledExportPlanPath(metadataPath);
        var folder = Path.GetDirectoryName(planPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("Styled export plan cannot be saved without an output folder."));
        }

        var tempPath = planPath + ".tmp";
        try
        {
            Directory.CreateDirectory(folder);
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, planPath, true);
            _logService.Info(
                "styled_export.plan_saved",
                "ScreenFast saved a styled export plan.",
                new Dictionary<string, object?>
                {
                    ["planPath"] = planPath,
                    ["metadataPath"] = metadataPath,
                    ["segmentCount"] = plan.Diagnostics.SegmentCount
                });
            return OperationResult<string>.Success(planPath);
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
                "styled_export.plan_save_failed",
                "ScreenFast could not save the styled export plan.",
                new Dictionary<string, object?>
                {
                    ["planPath"] = planPath,
                    ["metadataPath"] = metadataPath,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(AppError.ShellActionFailed($"ScreenFast could not save the styled export plan: {ex.Message}"));
        }
    }

    private async Task<OperationResult<AutoZoomPlan>> ReadZoomPlanAsync(string zoomPlanPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zoomPlanPath) || !File.Exists(zoomPlanPath))
        {
            return OperationResult<AutoZoomPlan>.Failure(AppError.SourceUnavailable("Auto-zoom plan file was not found."));
        }

        try
        {
            await using var stream = File.OpenRead(zoomPlanPath);
            var plan = await JsonSerializer.DeserializeAsync<AutoZoomPlan>(stream, JsonOptions, cancellationToken);
            return plan is null
                ? OperationResult<AutoZoomPlan>.Failure(AppError.SourceUnavailable("Auto-zoom plan file was empty or invalid."))
                : OperationResult<AutoZoomPlan>.Success(plan);
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "styled_export.zoom_plan_read_failed",
                "ScreenFast could not read the auto-zoom plan file.",
                new Dictionary<string, object?>
                {
                    ["zoomPlanPath"] = zoomPlanPath,
                    ["error"] = ex.Message
                });
            return OperationResult<AutoZoomPlan>.Failure(AppError.SourceUnavailable($"Auto-zoom plan could not be read: {ex.Message}"));
        }
    }
}
