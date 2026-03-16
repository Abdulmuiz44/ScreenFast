using Microsoft.UI.Dispatching;
using ScreenFast.App.Services;
using ScreenFast.App.Commands;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;

namespace ScreenFast.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IRecorderOrchestrator _orchestrator;
    private readonly IAppPreferencesService _preferencesService;
    private readonly IDesktopShellService _desktopShellService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly IReadOnlyList<VideoQualityPresetOption> _qualityPresets;
    private readonly IReadOnlyList<HotkeyModifierOption> _hotkeyModifiers;
    private readonly IReadOnlyList<HotkeyKeyOption> _hotkeyKeys;

    private RecorderStatusSnapshot _snapshot;
    private bool _includeSystemAudio;
    private bool _includeMicrophone;
    private string? _shellMessage;
    private VideoQualityPresetOption? _selectedQualityPreset;
    private bool _launchMinimizedToTray;
    private bool _closeToTray;
    private bool _minimizeToTray;
    private HotkeyModifierOption? _startHotkeyModifier;
    private HotkeyModifierOption? _stopHotkeyModifier;
    private HotkeyModifierOption? _pauseHotkeyModifier;
    private HotkeyKeyOption? _startHotkeyKey;
    private HotkeyKeyOption? _stopHotkeyKey;
    private HotkeyKeyOption? _pauseHotkeyKey;

    public MainWindowViewModel(
        IRecorderOrchestrator orchestrator,
        IAppPreferencesService preferencesService,
        IDesktopShellService desktopShellService)
    {
        _orchestrator = orchestrator;
        _preferencesService = preferencesService;
        _desktopShellService = desktopShellService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _snapshot = orchestrator.Snapshot;
        _includeSystemAudio = _snapshot.IncludeSystemAudio;
        _includeMicrophone = _snapshot.IncludeMicrophone;

        _qualityPresets = VideoQualityPresets.All
            .Select(definition => new VideoQualityPresetOption(definition.Preset, definition.DisplayName))
            .ToArray();
        _hotkeyModifiers = HotkeyModifierOption.CreateDefaults();
        _hotkeyKeys = HotkeyKeyOption.CreateDefaults();

        ApplySettings(_preferencesService.CurrentSettings);
        _selectedQualityPreset = FindQualityPreset(_snapshot.QualityPreset);

        SelectSourceCommand = new AsyncRelayCommand(() => _orchestrator.SelectSourceAsync(), () => CanSelectSource);
        PickOutputFolderCommand = new AsyncRelayCommand(() => _orchestrator.ChooseOutputFolderAsync(), () => CanPickOutputFolder);
        RecordCommand = new AsyncRelayCommand(() => _orchestrator.StartRecordingAsync(), () => CanRecord);
        PauseResumeCommand = new AsyncRelayCommand(() => _orchestrator.TogglePauseResumeAsync(), () => CanPauseResume);
        StopCommand = new AsyncRelayCommand(() => _orchestrator.StopRecordingAsync(), () => CanStop);
        ApplyHotkeysCommand = new AsyncRelayCommand(ApplyHotkeysAsync);

        _orchestrator.SnapshotChanged += OnSnapshotChanged;
        _preferencesService.SettingsChanged += OnSettingsChanged;
    }

    public AsyncRelayCommand SelectSourceCommand { get; }

    public AsyncRelayCommand PickOutputFolderCommand { get; }

    public AsyncRelayCommand RecordCommand { get; }

    public AsyncRelayCommand PauseResumeCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand ApplyHotkeysCommand { get; }

    public IReadOnlyList<VideoQualityPresetOption> QualityPresets => _qualityPresets;

    public IReadOnlyList<HotkeyModifierOption> HotkeyModifiers => _hotkeyModifiers;

    public IReadOnlyList<HotkeyKeyOption> HotkeyKeys => _hotkeyKeys;

    public string StatusText => _snapshot.StatusMessage;

    public string TimerText => _snapshot.TimerText;

    public string StateText => _snapshot.State.ToString();

    public string PauseResumeText => _snapshot.State == RecorderState.Paused ? "Resume" : "Pause";

    public string SourceSummary => _snapshot.SelectedSource is null
        ? "No source selected"
        : $"{_snapshot.SelectedSource.TypeDisplayName}: {_snapshot.SelectedSource.DisplayName}";

    public string SourceDetails => _snapshot.SelectedSource is null
        ? "Pick a display or app window."
        : _snapshot.SelectedSource.DimensionsText;

    public string SourceIdText => _snapshot.SelectedSource is null
        ? string.Empty
        : $"Source ID: {_snapshot.SelectedSource.SourceId}";

    public string OutputFolderText => string.IsNullOrWhiteSpace(_snapshot.OutputFolder)
        ? "Output folder not selected"
        : _snapshot.OutputFolder;

    public string ShellMessageText => _shellMessage ?? string.Empty;

    public bool IncludeSystemAudio
    {
        get => _includeSystemAudio;
        set
        {
            if (SetProperty(ref _includeSystemAudio, value))
            {
                _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone);
            }
        }
    }

    public bool IncludeMicrophone
    {
        get => _includeMicrophone;
        set
        {
            if (SetProperty(ref _includeMicrophone, value))
            {
                _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone);
            }
        }
    }

    public VideoQualityPresetOption? SelectedQualityPreset
    {
        get => _selectedQualityPreset;
        set
        {
            if (SetProperty(ref _selectedQualityPreset, value) && value is not null)
            {
                _orchestrator.UpdateQualityPreset(value.Preset);
            }
        }
    }

    public bool LaunchMinimizedToTray
    {
        get => _launchMinimizedToTray;
        set
        {
            if (SetProperty(ref _launchMinimizedToTray, value))
            {
                _ = SaveWindowBehaviorAsync();
            }
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (SetProperty(ref _closeToTray, value))
            {
                _ = SaveWindowBehaviorAsync();
            }
        }
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            if (SetProperty(ref _minimizeToTray, value))
            {
                _ = SaveWindowBehaviorAsync();
            }
        }
    }

    public HotkeyModifierOption? StartHotkeyModifier
    {
        get => _startHotkeyModifier;
        set => SetProperty(ref _startHotkeyModifier, value);
    }

    public HotkeyModifierOption? StopHotkeyModifier
    {
        get => _stopHotkeyModifier;
        set => SetProperty(ref _stopHotkeyModifier, value);
    }

    public HotkeyModifierOption? PauseHotkeyModifier
    {
        get => _pauseHotkeyModifier;
        set => SetProperty(ref _pauseHotkeyModifier, value);
    }

    public HotkeyKeyOption? StartHotkeyKey
    {
        get => _startHotkeyKey;
        set => SetProperty(ref _startHotkeyKey, value);
    }

    public HotkeyKeyOption? StopHotkeyKey
    {
        get => _stopHotkeyKey;
        set => SetProperty(ref _stopHotkeyKey, value);
    }

    public HotkeyKeyOption? PauseHotkeyKey
    {
        get => _pauseHotkeyKey;
        set => SetProperty(ref _pauseHotkeyKey, value);
    }

    public bool CanSelectSource => _snapshot.State is RecorderState.Idle or RecorderState.Ready or RecorderState.Error;

    public bool CanPickOutputFolder => _snapshot.State is RecorderState.Idle or RecorderState.Ready or RecorderState.Error;

    public bool CanRecord => _snapshot.State == RecorderState.Ready && !string.IsNullOrWhiteSpace(_snapshot.OutputFolder);

    public bool CanPauseResume => _snapshot.State is RecorderState.Recording or RecorderState.Paused;

    public bool CanStop => _snapshot.State is RecorderState.Recording or RecorderState.Paused;

    public void InitializeWindowHandle(nint windowHandle)
    {
        _orchestrator.SetWindowHandle(windowHandle);
    }

    public void SetShellMessage(string? message)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplyShellMessage(message));
            return;
        }

        ApplyShellMessage(message);
    }

    private async Task ApplyHotkeysAsync()
    {
        if (StartHotkeyModifier is null || StopHotkeyModifier is null || PauseHotkeyModifier is null || StartHotkeyKey is null || StopHotkeyKey is null || PauseHotkeyKey is null)
        {
            SetShellMessage("Select a modifier and function key for each ScreenFast hotkey.");
            return;
        }

        var result = await _desktopShellService.UpdateHotkeysAsync(
            new HotkeySettings(
                StartHotkeyModifier.CreateGesture(StartHotkeyKey.VirtualKey),
                StopHotkeyModifier.CreateGesture(StopHotkeyKey.VirtualKey),
                PauseHotkeyModifier.CreateGesture(PauseHotkeyKey.VirtualKey)));

        if (!result.IsSuccess)
        {
            ApplySettings(_preferencesService.CurrentSettings);
            SetShellMessage(result.Error?.Message ?? "ScreenFast could not update the hotkeys.");
            return;
        }

        SetShellMessage("ScreenFast hotkeys updated.");
    }

    private async Task SaveWindowBehaviorAsync()
    {
        var result = await _preferencesService.UpdateTrayBehaviorAsync(_launchMinimizedToTray, _closeToTray, _minimizeToTray);
        if (!result.IsSuccess)
        {
            SetShellMessage(result.Error?.Message ?? "ScreenFast could not save the tray behavior preferences.");
        }
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplySettings(settings));
            return;
        }

        ApplySettings(settings);
    }

    private void ApplySnapshot(RecorderStatusSnapshot snapshot)
    {
        _snapshot = snapshot;
        _includeSystemAudio = snapshot.IncludeSystemAudio;
        _includeMicrophone = snapshot.IncludeMicrophone;
        _selectedQualityPreset = FindQualityPreset(snapshot.QualityPreset);

        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(TimerText));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(PauseResumeText));
        RaisePropertyChanged(nameof(SourceSummary));
        RaisePropertyChanged(nameof(SourceDetails));
        RaisePropertyChanged(nameof(SourceIdText));
        RaisePropertyChanged(nameof(OutputFolderText));
        RaisePropertyChanged(nameof(IncludeSystemAudio));
        RaisePropertyChanged(nameof(IncludeMicrophone));
        RaisePropertyChanged(nameof(SelectedQualityPreset));
        RaisePropertyChanged(nameof(CanSelectSource));
        RaisePropertyChanged(nameof(CanPickOutputFolder));
        RaisePropertyChanged(nameof(CanRecord));
        RaisePropertyChanged(nameof(CanPauseResume));
        RaisePropertyChanged(nameof(CanStop));
        RefreshCommands();
    }

    private void ApplySettings(AppSettings settings)
    {
        _launchMinimizedToTray = settings.LaunchMinimizedToTray;
        _closeToTray = settings.CloseToTray;
        _minimizeToTray = settings.MinimizeToTray;
        _startHotkeyModifier = FindModifier(settings.Hotkeys.StartRecording);
        _stopHotkeyModifier = FindModifier(settings.Hotkeys.StopRecording);
        _pauseHotkeyModifier = FindModifier(settings.Hotkeys.PauseResumeRecording);
        _startHotkeyKey = FindHotkeyKey(settings.Hotkeys.StartRecording.VirtualKey);
        _stopHotkeyKey = FindHotkeyKey(settings.Hotkeys.StopRecording.VirtualKey);
        _pauseHotkeyKey = FindHotkeyKey(settings.Hotkeys.PauseResumeRecording.VirtualKey);

        RaisePropertyChanged(nameof(LaunchMinimizedToTray));
        RaisePropertyChanged(nameof(CloseToTray));
        RaisePropertyChanged(nameof(MinimizeToTray));
        RaisePropertyChanged(nameof(StartHotkeyModifier));
        RaisePropertyChanged(nameof(StopHotkeyModifier));
        RaisePropertyChanged(nameof(PauseHotkeyModifier));
        RaisePropertyChanged(nameof(StartHotkeyKey));
        RaisePropertyChanged(nameof(StopHotkeyKey));
        RaisePropertyChanged(nameof(PauseHotkeyKey));
    }

    private void ApplyShellMessage(string? message)
    {
        _shellMessage = message;
        RaisePropertyChanged(nameof(ShellMessageText));
    }

    private VideoQualityPresetOption FindQualityPreset(VideoQualityPreset preset)
    {
        return _qualityPresets.First(option => option.Preset == preset);
    }

    private HotkeyModifierOption FindModifier(HotkeyGesture gesture)
    {
        return _hotkeyModifiers.FirstOrDefault(option => option.Matches(gesture)) ?? _hotkeyModifiers[0];
    }

    private HotkeyKeyOption FindHotkeyKey(int virtualKey)
    {
        return _hotkeyKeys.FirstOrDefault(option => option.VirtualKey == virtualKey) ?? _hotkeyKeys[0];
    }

    private void RefreshCommands()
    {
        SelectSourceCommand.NotifyCanExecuteChanged();
        PickOutputFolderCommand.NotifyCanExecuteChanged();
        RecordCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ApplyHotkeysCommand.NotifyCanExecuteChanged();
    }
}

public sealed record VideoQualityPresetOption(VideoQualityPreset Preset, string Label);

public sealed record HotkeyModifierOption(string Label, bool Control, bool Shift, bool Alt)
{
    public static IReadOnlyList<HotkeyModifierOption> CreateDefaults() =>
    [
        new("Ctrl + Shift", true, true, false),
        new("Ctrl + Alt", true, false, true),
        new("Alt + Shift", false, true, true),
        new("Ctrl + Alt + Shift", true, true, true)
    ];

    public bool Matches(HotkeyGesture gesture) =>
        gesture.Control == Control &&
        gesture.Shift == Shift &&
        gesture.Alt == Alt;

    public HotkeyGesture CreateGesture(int virtualKey) => new(Control, Shift, Alt, virtualKey);
}

public sealed record HotkeyKeyOption(int VirtualKey, string Label)
{
    public static IReadOnlyList<HotkeyKeyOption> CreateDefaults() =>
        Enumerable.Range(1, 24)
            .Select(index => new HotkeyKeyOption(0x6F + index, $"F{index}"))
            .ToArray();
}


