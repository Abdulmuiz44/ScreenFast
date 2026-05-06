using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IStyledVideoExportService
{
    Task<OperationResult<StyledVideoExportResult>> ExportAsync(
        string styledExportPlanPath,
        CancellationToken cancellationToken = default);
}
