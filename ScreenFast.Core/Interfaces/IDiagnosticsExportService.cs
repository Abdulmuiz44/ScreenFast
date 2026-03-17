using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IDiagnosticsExportService
{
    Task<OperationResult<string>> ExportAsync(
        nint ownerWindowHandle,
        AppSettings settings,
        RecorderStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
