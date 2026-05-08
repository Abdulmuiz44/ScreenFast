using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class UnsupportedStyledExportService : IStyledExportService
{
    private readonly IScreenFastLogService _logService;

    public UnsupportedStyledExportService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public Task<OperationResult<string?>> TryCreateStyledExportAsync(
        string rawVideoPath,
        string? metadataSidecarPath,
        string? zoomPlanPath,
        ExportProfile exportProfile,
        CancellationToken cancellationToken = default)
    {
        _logService.Info(
            "styled_export.skipped_renderer_unavailable",
            "ScreenFast skipped styled export because the second-pass renderer is not implemented in this build.",
            new Dictionary<string, object?>
            {
                ["rawVideoPath"] = rawVideoPath,
                ["metadataSidecarPath"] = metadataSidecarPath,
                ["zoomPlanPath"] = zoomPlanPath,
                ["exportProfile"] = exportProfile.DisplayName
            });
        return Task.FromResult(OperationResult<string?>.Success(null));
    }
}
