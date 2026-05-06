using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class AutoZoomPlanService : IAutoZoomPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRecordingMetadataReader _metadataReader;
    private readonly IAutoZoomPlanner _planner;
    private readonly IScreenFastLogService _logService;

    public AutoZoomPlanService(
        IRecordingMetadataReader metadataReader,
        IAutoZoomPlanner planner,
        IScreenFastLogService logService)
    {
        _metadataReader = metadataReader;
        _planner = planner;
        _logService = logService;
    }

    public async Task<OperationResult<AutoZoomPlan>> PlanFromSidecarAsync(
        string metadataPath,
        AutoZoomPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var readResult = await _metadataReader.ReadAsync(metadataPath, cancellationToken);
        if (!readResult.IsSuccess || readResult.Value is null)
        {
            return OperationResult<AutoZoomPlan>.Failure(readResult.Error!);
        }

        var planResult = _planner.Plan(new AutoZoomPlannerInput(readResult.Value, options ?? AutoZoomPlannerOptions.Create()));
        if (!planResult.IsSuccess || planResult.Value is null)
        {
            _logService.Warning(
                "zoom_plan.create_failed",
                "ScreenFast could not create an auto-zoom plan from recording metadata.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = metadataPath,
                    ["error"] = planResult.Error?.Message
                });
            return planResult;
        }

        _logService.Info(
            "zoom_plan.created",
            "ScreenFast created an auto-zoom plan from recording metadata.",
            new Dictionary<string, object?>
            {
                ["metadataPath"] = metadataPath,
                ["segmentCount"] = planResult.Value.Diagnostics.SegmentCount,
                ["cursorSamplesConsumed"] = planResult.Value.Diagnostics.CursorSamplesConsumed,
                ["clickEventsInfluencedPlan"] = planResult.Value.Diagnostics.ClickEventsInfluencedPlan
            });
        return planResult;
    }

    public string GetPlanPath(string metadataPath)
    {
        if (metadataPath.EndsWith(".screenfast.json", StringComparison.OrdinalIgnoreCase))
        {
            return metadataPath[..^".screenfast.json".Length] + ".zoomplan.json";
        }

        return Path.ChangeExtension(metadataPath, ".zoomplan.json");
    }

    public async Task<OperationResult<string>> SavePlanAsync(
        AutoZoomPlan plan,
        string metadataPath,
        CancellationToken cancellationToken = default)
    {
        var planPath = GetPlanPath(metadataPath);
        var folder = Path.GetDirectoryName(planPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("Auto-zoom plan cannot be saved without an output folder."));
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
                "zoom_plan.saved",
                "ScreenFast saved an auto-zoom plan.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = metadataPath,
                    ["planPath"] = planPath,
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
                "zoom_plan.save_failed",
                "ScreenFast could not save the auto-zoom plan.",
                new Dictionary<string, object?>
                {
                    ["metadataPath"] = metadataPath,
                    ["planPath"] = planPath,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(AppError.ShellActionFailed($"ScreenFast could not save the auto-zoom plan: {ex.Message}"));
        }
    }
}
