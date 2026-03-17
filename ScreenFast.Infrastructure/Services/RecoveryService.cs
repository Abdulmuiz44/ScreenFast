using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecoveryService : IRecoveryService
{
    private readonly IRecoveryStateStore _recoveryStateStore;
    private readonly IScreenFastLogService _logService;
    private bool _isInitialized;

    public RecoveryService(IRecoveryStateStore recoveryStateStore, IScreenFastLogService logService)
    {
        _recoveryStateStore = recoveryStateStore;
        _logService = logService;
    }

    public event EventHandler<RecoverySessionMarker?>? RecoveryStateChanged;

    public RecoverySessionMarker? CurrentInterruptedSession { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        CurrentInterruptedSession = await _recoveryStateStore.LoadAsync(cancellationToken);
        if (CurrentInterruptedSession is not null)
        {
            _logService.Warning(
                "recovery.detected",
                "ScreenFast detected an interrupted recording session.",
                BuildProperties(CurrentInterruptedSession));
        }
        else
        {
            _logService.Info("recovery.none", "No interrupted recording session was detected.");
        }

        RecoveryStateChanged?.Invoke(this, CurrentInterruptedSession);
    }

    public async Task MarkSessionStartedAsync(RecoverySessionMarker marker, CancellationToken cancellationToken = default)
    {
        try
        {
            await _recoveryStateStore.SaveAsync(marker, cancellationToken);
            CurrentInterruptedSession = null;
            _logService.Info("recovery.marker_created", "ScreenFast wrote the active session marker.", BuildProperties(marker));
            RecoveryStateChanged?.Invoke(this, CurrentInterruptedSession);
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "recovery.marker_create_failed",
                "ScreenFast could not persist the active session marker.",
                new Dictionary<string, object?>
                {
                    ["sessionId"] = marker.SessionId,
                    ["error"] = ex.Message
                });
        }
    }

    public async Task ClearActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _recoveryStateStore.ClearAsync(cancellationToken);
            CurrentInterruptedSession = null;
            _logService.Info("recovery.marker_cleared", "ScreenFast cleared the active session marker.");
            RecoveryStateChanged?.Invoke(this, CurrentInterruptedSession);
        }
        catch (Exception ex)
        {
            _logService.Warning(
                "recovery.marker_clear_failed",
                "ScreenFast could not clear the active session marker.",
                new Dictionary<string, object?> { ["error"] = ex.Message });
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildProperties(RecoverySessionMarker marker)
    {
        return new Dictionary<string, object?>
        {
            ["sessionId"] = marker.SessionId,
            ["startedAtUtc"] = marker.StartedAtUtc,
            ["sourceSummary"] = marker.SourceSummary,
            ["targetFilePath"] = marker.TargetFilePath,
            ["qualityPreset"] = marker.QualityPreset,
            ["includeSystemAudio"] = marker.IncludeSystemAudio,
            ["includeMicrophone"] = marker.IncludeMicrophone
        };
    }
}
