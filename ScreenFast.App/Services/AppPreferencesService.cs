using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public sealed class AppPreferencesService : IAppPreferencesService, IDisposable
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IRecorderOrchestrator _recorderOrchestrator;
    private readonly ICaptureItemResolver _captureItemResolver;
    private readonly IScreenFastLogService _logService;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private bool _isInitialized;
    private bool _isDisposed;

    public AppPreferencesService(
        IAppSettingsStore settingsStore,
        IRecorderOrchestrator recorderOrchestrator,
        ICaptureItemResolver captureItemResolver,
        IScreenFastLogService logService)
    {
        _settingsStore = settingsStore;
        _recorderOrchestrator = recorderOrchestrator;
        _captureItemResolver = captureItemResolver;
        _logService = logService;
        CurrentSettings = AppSettings.CreateDefault();
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings CurrentSettings { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        var loadResult = await _settingsStore.LoadAsync(cancellationToken);
        var settings = loadResult.Settings;
        var messages = new List<string>();

        if (!string.IsNullOrWhiteSpace(loadResult.WarningMessage))
        {
            messages.Add(loadResult.WarningMessage);
            _logService.Warning("settings.load_warning", loadResult.WarningMessage);
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputFolder) && !Directory.Exists(settings.OutputFolder))
        {
            settings = settings with { OutputFolder = null };
            messages.Add("The saved output folder was missing, so ScreenFast cleared it.");
            _logService.Warning("settings.output_folder_missing", "ScreenFast cleared the saved output folder because it no longer exists.");
        }

        CaptureSourceModel? restoredSource = null;
        if (settings.LastSelectedSource is not null)
        {
            var sourceResult = _captureItemResolver.Resolve(settings.LastSelectedSource);
            if (sourceResult.IsSuccess)
            {
                restoredSource = settings.LastSelectedSource;
                messages.Add("Restored the previous capture source.");
                _logService.Info(
                    "settings.source_restored",
                    "ScreenFast restored the previous capture source.",
                    new Dictionary<string, object?>
                    {
                        ["sourceSummary"] = $"{restoredSource.TypeDisplayName}: {restoredSource.DisplayName}",
                        ["sourceId"] = restoredSource.SourceId
                    });
            }
            else
            {
                settings = settings with { LastSelectedSource = null };
                messages.Add("The previous capture source is no longer available. Select a new one.");
                _logService.Warning(
                    "settings.source_restore_failed",
                    "The saved capture source could not be restored.",
                    new Dictionary<string, object?> { ["error"] = sourceResult.Error?.Message });
            }
        }

        CurrentSettings = settings;
        _recorderOrchestrator.ApplyPersistedSettings(settings, restoredSource, messages.Count == 0 ? null : string.Join(" ", messages));

        _recorderOrchestrator.SnapshotChanged += OnSnapshotChanged;
        _isInitialized = true;
        SettingsChanged?.Invoke(this, CurrentSettings);
        _logService.Info("settings.loaded", "ScreenFast loaded app settings successfully.");

        await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateHotkeySettingsAsync(HotkeySettings hotkeys, CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with { Hotkeys = hotkeys };
        SettingsChanged?.Invoke(this, CurrentSettings);
        _logService.Info(
            "settings.hotkeys_updated",
            "ScreenFast updated the hotkey settings.",
            new Dictionary<string, object?>
            {
                ["start"] = hotkeys.StartRecording.DisplayText,
                ["stop"] = hotkeys.StopRecording.DisplayText,
                ["pauseResume"] = hotkeys.PauseResumeRecording.DisplayText
            });
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateTrayBehaviorAsync(bool launchMinimizedToTray, bool closeToTray, bool minimizeToTray, CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with
        {
            LaunchMinimizedToTray = launchMinimizedToTray,
            CloseToTray = closeToTray,
            MinimizeToTray = minimizeToTray
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        _logService.Info(
            "settings.tray_behavior_updated",
            "ScreenFast updated tray behavior preferences.",
            new Dictionary<string, object?>
            {
                ["launchMinimizedToTray"] = launchMinimizedToTray,
                ["closeToTray"] = closeToTray,
                ["minimizeToTray"] = minimizeToTray
            });
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateRecorderPreferencesAsync(
        bool includeSystemAudio,
        bool includeMicrophone,
        VideoQualityPreset qualityPreset,
        PostRecordingOpenBehavior postRecordingOpenBehavior,
        RecordingCountdownOption countdownOption,
        bool overlayEnabled,
        bool isOnboardingDismissed,
        CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with
        {
            IncludeSystemAudio = includeSystemAudio,
            IncludeMicrophone = includeMicrophone,
            QualityPreset = qualityPreset,
            PostRecordingOpenBehavior = postRecordingOpenBehavior,
            CountdownOption = countdownOption,
            OverlayEnabled = overlayEnabled,
            IsOnboardingDismissed = isOnboardingDismissed
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        _logService.Info(
            "settings.recorder_preferences_updated",
            "ScreenFast updated recorder preferences.",
            new Dictionary<string, object?>
            {
                ["includeSystemAudio"] = includeSystemAudio,
                ["includeMicrophone"] = includeMicrophone,
                ["qualityPreset"] = qualityPreset,
                ["postRecordingOpenBehavior"] = postRecordingOpenBehavior,
                ["countdownOption"] = countdownOption,
                ["overlayEnabled"] = overlayEnabled,
                ["isOnboardingDismissed"] = isOnboardingDismissed
            });
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateDismissedRecoverySessionAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with
        {
            DismissedRecoverySessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        _logService.Info(
            "settings.recovery_dismissal_updated",
            "ScreenFast updated the dismissed recovery marker id.",
            new Dictionary<string, object?> { ["sessionId"] = CurrentSettings.DismissedRecoverySessionId });
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _recorderOrchestrator.SnapshotChanged -= OnSnapshotChanged;
        _saveGate.Dispose();
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        CurrentSettings = CurrentSettings with
        {
            OutputFolder = snapshot.OutputFolder,
            IncludeSystemAudio = snapshot.IncludeSystemAudio,
            IncludeMicrophone = snapshot.IncludeMicrophone,
            QualityPreset = snapshot.QualityPreset,
            PostRecordingOpenBehavior = snapshot.PostRecordingOpenBehavior,
            CountdownOption = snapshot.CountdownOption,
            OverlayEnabled = snapshot.OverlayEnabled,
            LastSelectedSource = snapshot.SelectedSource
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        _ = PersistAsync(CurrentSettings, CancellationToken.None);
    }

    private async Task<OperationResult> PersistAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return OperationResult.Success();
        }

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var result = await _settingsStore.SaveAsync(settings, cancellationToken);
            if (!result.IsSuccess)
            {
                _logService.Warning(
                    "settings.save_failed",
                    "ScreenFast could not save settings.",
                    new Dictionary<string, object?> { ["error"] = result.Error?.Message });
            }

            return result;
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
