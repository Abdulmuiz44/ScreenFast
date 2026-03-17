using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecoveryStateStore
{
    Task<RecoverySessionMarker?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(RecoverySessionMarker marker, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
