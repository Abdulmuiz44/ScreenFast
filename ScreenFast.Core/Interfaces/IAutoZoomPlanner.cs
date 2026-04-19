using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IAutoZoomPlanner
{
    OperationResult<AutoZoomPlan> Plan(AutoZoomPlannerInput input);
}
