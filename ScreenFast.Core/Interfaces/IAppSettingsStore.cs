using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IAppSettingsStore
{
    Task<AppSettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
