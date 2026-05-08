using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IStyledExportService
{
    Task<OperationResult<string?>> TryCreateStyledExportAsync(
        string rawVideoPath,
        string? metadataSidecarPath,
        string? zoomPlanPath,
        ExportProfile exportProfile,
        CancellationToken cancellationToken = default);
}
