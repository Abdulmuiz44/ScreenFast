using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecoveryService
{
    event EventHandler<RecoverySessionMarker?>? RecoveryStateChanged;

    RecoverySessionMarker? CurrentInterruptedSession { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task MarkSessionStartedAsync(RecoverySessionMarker marker, CancellationToken cancellationToken = default);

    Task ClearActiveSessionAsync(CancellationToken cancellationToken = default);
}
