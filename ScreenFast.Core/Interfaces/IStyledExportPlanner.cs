using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IStyledExportPlanner
{
    OperationResult<StyledExportPlan> Plan(
        RecordingRenderInput renderInput,
        AutoZoomPlan zoomPlan,
        StyledExportCompositionSettings composition,
        string zoomPlanPath,
        string? suggestedOutputVideoPath = null);
}
