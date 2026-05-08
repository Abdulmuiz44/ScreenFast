using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface ISettingsBackupService
{
    Task<OperationResult<string>> ExportAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<OperationResult<AppSettings>> ImportLatestAsync(CancellationToken cancellationToken = default);
}
