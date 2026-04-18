using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IAutoZoomPlanService
{
    Task<OperationResult<AutoZoomPlan>> PlanFromSidecarAsync(
        string metadataPath,
        AutoZoomPlannerOptions? options = null,
        CancellationToken cancellationToken = default);

    string GetPlanPath(string metadataPath);

    Task<OperationResult<string>> SavePlanAsync(
        AutoZoomPlan plan,
        string metadataPath,
        CancellationToken cancellationToken = default);
}
