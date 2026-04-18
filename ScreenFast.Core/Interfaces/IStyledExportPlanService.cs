using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IStyledExportPlanService
{
    Task<OperationResult<StyledExportPlan>> PlanFromArtifactsAsync(
        string metadataPath,
        string zoomPlanPath,
        StyledExportCompositionSettings? composition = null,
        string? suggestedOutputVideoPath = null,
        CancellationToken cancellationToken = default);

    string GetStyledExportPlanPath(string metadataPath);

    Task<OperationResult<string>> SavePlanAsync(
        StyledExportPlan plan,
        string metadataPath,
        CancellationToken cancellationToken = default);
}
