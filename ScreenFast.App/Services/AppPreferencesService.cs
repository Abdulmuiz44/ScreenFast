using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public sealed class AppPreferencesService : IAppPreferencesService
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IRecorderOrchestrator _recorderOrchestrator;
    private readonly ICaptureItemResolver _captureItemResolver;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private bool _isInitialized;

    public AppPreferencesService(
        IAppSettingsStore settingsStore,
        IRecorderOrchestrator recorderOrchestrator,
        ICaptureItemResolver captureItemResolver)
    {
        _settingsStore = settingsStore;
        _recorderOrchestrator = recorderOrchestrator;
        _captureItemResolver = captureItemResolver;
        CurrentSettings = AppSettings.CreateDefault();
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings CurrentSettings { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        var loadResult = await _settingsStore.LoadAsync(cancellationToken);
        var settings = loadResult.Settings;
        var messages = new List<string>();

        if (!string.IsNullOrWhiteSpace(loadResult.WarningMessage))
        {
            messages.Add(loadResult.WarningMessage);
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputFolder) && !Directory.Exists(settings.OutputFolder))
        {
            settings = settings with { OutputFolder = null };
            messages.Add("The saved output folder was missing, so ScreenFast cleared it.");
        }

        CaptureSourceModel? restoredSource = null;
        if (settings.LastSelectedSource is not null)
        {
            var sourceResult = _captureItemResolver.Resolve(settings.LastSelectedSource);
            if (sourceResult.IsSuccess)
            {
                restoredSource = settings.LastSelectedSource;
                messages.Add("Restored the previous capture source.");
            }
            else
            {
                settings = settings with { LastSelectedSource = null };
                messages.Add("The previous capture source is no longer available. Select a new one.");
            }
        }

        CurrentSettings = settings;
        _recorderOrchestrator.ApplyPersistedSettings(
            settings,
            restoredSource,
            messages.Count == 0 ? null : string.Join(" ", messages));

        _recorderOrchestrator.SnapshotChanged += OnSnapshotChanged;
        _isInitialized = true;
        SettingsChanged?.Invoke(this, CurrentSettings);

        await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateHotkeySettingsAsync(HotkeySettings hotkeys, CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with { Hotkeys = hotkeys };
        SettingsChanged?.Invoke(this, CurrentSettings);
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
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    public async Task<OperationResult> UpdateRecorderPreferencesAsync(
        bool includeSystemAudio,
        bool includeMicrophone,
        VideoQualityPreset qualityPreset,
        PostRecordingOpenBehavior postRecordingOpenBehavior,
        bool isOnboardingDismissed,
        CancellationToken cancellationToken = default)
    {
        CurrentSettings = CurrentSettings with
        {
            IncludeSystemAudio = includeSystemAudio,
            IncludeMicrophone = includeMicrophone,
            QualityPreset = qualityPreset,
            PostRecordingOpenBehavior = postRecordingOpenBehavior,
            IsOnboardingDismissed = isOnboardingDismissed
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        return await PersistAsync(CurrentSettings, cancellationToken);
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        if (!_isInitialized)
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
            LastSelectedSource = snapshot.SelectedSource
        };

        SettingsChanged?.Invoke(this, CurrentSettings);
        _ = PersistAsync(CurrentSettings, CancellationToken.None);
    }

    private async Task<OperationResult> PersistAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            return await _settingsStore.SaveAsync(settings, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
