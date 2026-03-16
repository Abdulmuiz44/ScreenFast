using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Dispatching;
using ScreenFast.App.Commands;
using ScreenFast.App.Services;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.State;
using DataTransfer = Windows.ApplicationModel.DataTransfer;

namespace ScreenFast.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IRecorderOrchestrator _orchestrator;
    private readonly IRecordingHistoryService _historyService;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IDesktopShellService _desktopShellService;
    private readonly IAppPreferencesService _preferencesService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly IReadOnlyList<VideoQualityPresetOption> _qualityPresets;
    private readonly IReadOnlyList<PostRecordingOpenBehaviorOption> _postRecordingBehaviors;
    private readonly IReadOnlyList<HotkeyModifierOption> _hotkeyModifiers;
    private readonly IReadOnlyList<HotkeyKeyOption> _hotkeyKeys;

    private RecorderStatusSnapshot _snapshot;
    private bool _includeSystemAudio;
    private bool _includeMicrophone;
    private string? _shellMessage;
    private VideoQualityPresetOption? _selectedQualityPreset;
    private PostRecordingOpenBehaviorOption? _selectedPostRecordingBehavior;
    private bool _launchMinimizedToTray;
    private bool _closeToTray;
    private bool _minimizeToTray;
    private HotkeyModifierOption? _startHotkeyModifier;
    private HotkeyModifierOption? _stopHotkeyModifier;
    private HotkeyModifierOption? _pauseHotkeyModifier;
    private HotkeyKeyOption? _startHotkeyKey;
    private HotkeyKeyOption? _stopHotkeyKey;
    private HotkeyKeyOption? _pauseHotkeyKey;
    private RecordingHistoryItemViewModel? _selectedRecentRecording;
    private bool _isOnboardingDismissed;

    public MainWindowViewModel(
        IRecorderOrchestrator orchestrator,
        IRecordingHistoryService historyService,
        IFileLauncherService fileLauncherService,
        IDesktopShellService desktopShellService,
        IAppPreferencesService preferencesService)
    {
        _orchestrator = orchestrator;
        _historyService = historyService;
        _fileLauncherService = fileLauncherService;
        _desktopShellService = desktopShellService;
        _preferencesService = preferencesService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _snapshot = orchestrator.Snapshot;
        _qualityPresets = VideoQualityPresets.All
            .Select(definition => new VideoQualityPresetOption(definition.Preset, definition.DisplayName))
            .ToArray();
        _postRecordingBehaviors =
        [
            new(PostRecordingOpenBehavior.None, "Do Nothing"),
            new(PostRecordingOpenBehavior.OpenFile, "Open Recording"),
            new(PostRecordingOpenBehavior.OpenContainingFolder, "Open Folder")
        ];
        _hotkeyModifiers = HotkeyModifierOption.CreateDefaults();
        _hotkeyKeys = HotkeyKeyOption.CreateDefaults();
        RecentRecordings = new ObservableCollection<RecordingHistoryItemViewModel>();

        ApplySettings(_preferencesService.CurrentSettings);
        _includeSystemAudio = _snapshot.IncludeSystemAudio;
        _includeMicrophone = _snapshot.IncludeMicrophone;
        _selectedQualityPreset = FindQualityPreset(_snapshot.QualityPreset);
        _selectedPostRecordingBehavior = FindPostRecordingBehavior(_snapshot.PostRecordingOpenBehavior);

        SelectSourceCommand = new AsyncRelayCommand(() => _orchestrator.SelectSourceAsync(), () => CanSelectSource);
        PickOutputFolderCommand = new AsyncRelayCommand(() => _orchestrator.ChooseOutputFolderAsync(), () => CanPickOutputFolder);
        RecordCommand = new AsyncRelayCommand(() => _orchestrator.StartRecordingAsync(), () => CanRecord);
        PauseResumeCommand = new AsyncRelayCommand(() => _orchestrator.TogglePauseResumeAsync(), () => CanPauseResume);
        StopCommand = new AsyncRelayCommand(() => _orchestrator.StopRecordingAsync(), () => CanStop);
        ApplyHotkeysCommand = new AsyncRelayCommand(ApplyHotkeysAsync);
        OpenRecordingCommand = new AsyncRelayCommand(OpenRecordingAsync, () => SelectedRecentRecording is not null);
        OpenContainingFolderCommand = new AsyncRelayCommand(OpenContainingFolderAsync, () => SelectedRecentRecording is not null);
        CopyPathCommand = new RelayCommand(CopyPath, () => SelectedRecentRecording is not null);
        RemoveHistoryItemCommand = new AsyncRelayCommand(RemoveHistoryItemAsync, () => SelectedRecentRecording is not null);
        ClearMissingHistoryCommand = new AsyncRelayCommand(ClearMissingHistoryAsync, () => RecentRecordings.Count > 0);
        ClearAllHistoryCommand = new AsyncRelayCommand(ClearAllHistoryAsync, () => RecentRecordings.Count > 0);
        DismissOnboardingCommand = new AsyncRelayCommand(DismissOnboardingAsync, () => IsOnboardingVisible);
        ShowOnboardingAgainCommand = new AsyncRelayCommand(ShowOnboardingAgainAsync, () => !IsOnboardingVisible);

        _orchestrator.SnapshotChanged += OnSnapshotChanged;
        _preferencesService.SettingsChanged += OnSettingsChanged;
    }

    public AsyncRelayCommand SelectSourceCommand { get; }
    public AsyncRelayCommand PickOutputFolderCommand { get; }
    public AsyncRelayCommand RecordCommand { get; }
    public AsyncRelayCommand PauseResumeCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand ApplyHotkeysCommand { get; }
    public AsyncRelayCommand OpenRecordingCommand { get; }
    public AsyncRelayCommand OpenContainingFolderCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public AsyncRelayCommand RemoveHistoryItemCommand { get; }
    public AsyncRelayCommand ClearMissingHistoryCommand { get; }
    public AsyncRelayCommand ClearAllHistoryCommand { get; }
    public AsyncRelayCommand DismissOnboardingCommand { get; }
    public AsyncRelayCommand ShowOnboardingAgainCommand { get; }

    public ObservableCollection<RecordingHistoryItemViewModel> RecentRecordings { get; }

    public IReadOnlyList<VideoQualityPresetOption> QualityPresets => _qualityPresets;

    public IReadOnlyList<PostRecordingOpenBehaviorOption> PostRecordingBehaviors => _postRecordingBehaviors;

    public IReadOnlyList<HotkeyModifierOption> HotkeyModifiers => _hotkeyModifiers;

    public IReadOnlyList<HotkeyKeyOption> HotkeyKeys => _hotkeyKeys;

    public RecordingHistoryItemViewModel? SelectedRecentRecording
    {
        get => _selectedRecentRecording;
        set
        {
            if (SetProperty(ref _selectedRecentRecording, value))
            {
                RefreshCommands();
            }
        }
    }

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

    public bool IsOnboardingVisible => !_isOnboardingDismissed;

    public string ReadySourceText => IsSourceReady ? "Source selected" : "Source missing";

    public string ReadyOutputFolderText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_snapshot.OutputFolder))
            {
                return "Output folder missing";
            }

            return Directory.Exists(_snapshot.OutputFolder)
                ? "Output folder available"
                : "Output folder unavailable";
        }
    }

    public string AudioChoicesSummary
    {
        get
        {
            var parts = new List<string>();
            if (_includeSystemAudio)
            {
                parts.Add("System audio");
            }

            if (_includeMicrophone)
            {
                parts.Add("Microphone");
            }

            return parts.Count == 0 ? "Audio: none" : $"Audio: {string.Join(" + ", parts)}";
        }
    }

    public string RecentRecordingsStatusText => HasRecentRecordings
        ? "Newest first. Missing files remain listed until removed."
        : "No recordings yet. Finished recordings will appear here.";

    public bool HasRecentRecordings => RecentRecordings.Count > 0;

    public bool IncludeSystemAudio
    {
        get => _includeSystemAudio;
        set
        {
            if (SetProperty(ref _includeSystemAudio, value))
            {
                _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone, SelectedPostRecordingBehavior?.Behavior ?? PostRecordingOpenBehavior.None);
                RaisePropertyChanged(nameof(AudioChoicesSummary));
                _ = SavePreferencesAsync();
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
                _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone, SelectedPostRecordingBehavior?.Behavior ?? PostRecordingOpenBehavior.None);
                RaisePropertyChanged(nameof(AudioChoicesSummary));
                _ = SavePreferencesAsync();
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
                _ = SavePreferencesAsync();
            }
        }
    }

    public PostRecordingOpenBehaviorOption? SelectedPostRecordingBehavior
    {
        get => _selectedPostRecordingBehavior;
        set
        {
            if (SetProperty(ref _selectedPostRecordingBehavior, value) && value is not null)
            {
                _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone, value.Behavior);
                _ = SavePreferencesAsync();
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

    public bool CanRecord => _snapshot.State == RecorderState.Ready && IsSourceReady && !string.IsNullOrWhiteSpace(_snapshot.OutputFolder);

    public bool CanPauseResume => _snapshot.State is RecorderState.Recording or RecorderState.Paused;

    public bool CanStop => _snapshot.State is RecorderState.Recording or RecorderState.Paused;

    private bool IsSourceReady => _snapshot.SelectedSource is not null && _snapshot.SelectedSource.Width > 0 && _snapshot.SelectedSource.Height > 0;

    public void InitializeWindowHandle(nint windowHandle) => _orchestrator.SetWindowHandle(windowHandle);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ApplySettings(_preferencesService.CurrentSettings);
        _orchestrator.UpdateAudioPreferences(_includeSystemAudio, _includeMicrophone, SelectedPostRecordingBehavior?.Behavior ?? PostRecordingOpenBehavior.None);
        _orchestrator.UpdateQualityPreset(SelectedQualityPreset?.Preset ?? VideoQualityPreset.Standard);

        await _historyService.RefreshAvailabilityAsync(cancellationToken);
        await ReloadHistoryAsync(cancellationToken);
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

    private async Task ReloadHistoryAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _historyService.GetRecentAsync(cancellationToken);
        RecentRecordings.Clear();
        foreach (var entry in entries)
        {
            RecentRecordings.Add(new RecordingHistoryItemViewModel(entry));
        }

        if (SelectedRecentRecording is not null)
        {
            SelectedRecentRecording = RecentRecordings.FirstOrDefault(x => x.Id == SelectedRecentRecording.Id);
        }

        RaisePropertyChanged(nameof(HasRecentRecordings));
        RaisePropertyChanged(nameof(RecentRecordingsStatusText));
        RefreshCommands();
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

    private async Task SavePreferencesAsync()
    {
        var result = await _preferencesService.UpdateRecorderPreferencesAsync(
            _includeSystemAudio,
            _includeMicrophone,
            SelectedQualityPreset?.Preset ?? VideoQualityPreset.Standard,
            SelectedPostRecordingBehavior?.Behavior ?? PostRecordingOpenBehavior.None,
            _isOnboardingDismissed);

        if (!result.IsSuccess)
        {
            SetShellMessage(result.Error?.Message ?? "ScreenFast could not save preferences, but recording can continue.");
        }
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        var shouldReloadHistory = ShouldReloadHistory(snapshot);
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                await ApplySnapshotAsync(snapshot);
                if (shouldReloadHistory)
                {
                    await ReloadHistoryAsync();
                }
            });
            return;
        }

        _ = ApplySnapshotAsync(snapshot);
        if (shouldReloadHistory)
        {
            _ = ReloadHistoryAsync();
        }
    }

    private bool ShouldReloadHistory(RecorderStatusSnapshot nextSnapshot)
    {
        return nextSnapshot.State is RecorderState.Ready or RecorderState.Error
            && _snapshot.State is RecorderState.Stopping or RecorderState.Recording or RecorderState.Paused;
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

    private async Task ApplySnapshotAsync(RecorderStatusSnapshot snapshot)
    {
        _snapshot = snapshot;
        _includeSystemAudio = snapshot.IncludeSystemAudio;
        _includeMicrophone = snapshot.IncludeMicrophone;
        _selectedQualityPreset = FindQualityPreset(snapshot.QualityPreset);
        _selectedPostRecordingBehavior = FindPostRecordingBehavior(snapshot.PostRecordingOpenBehavior);

        if (snapshot.State == RecorderState.Recording && !_isOnboardingDismissed)
        {
            _isOnboardingDismissed = true;
            await SavePreferencesAsync();
        }

        RaiseAllStateProperties();
    }

    private void ApplySettings(AppSettings settings)
    {
        _includeSystemAudio = settings.IncludeSystemAudio;
        _includeMicrophone = settings.IncludeMicrophone;
        _selectedQualityPreset = FindQualityPreset(settings.QualityPreset);
        _selectedPostRecordingBehavior = FindPostRecordingBehavior(settings.PostRecordingOpenBehavior);
        _launchMinimizedToTray = settings.LaunchMinimizedToTray;
        _closeToTray = settings.CloseToTray;
        _minimizeToTray = settings.MinimizeToTray;
        _isOnboardingDismissed = settings.IsOnboardingDismissed;
        _startHotkeyModifier = FindModifier(settings.Hotkeys.StartRecording);
        _stopHotkeyModifier = FindModifier(settings.Hotkeys.StopRecording);
        _pauseHotkeyModifier = FindModifier(settings.Hotkeys.PauseResumeRecording);
        _startHotkeyKey = FindHotkeyKey(settings.Hotkeys.StartRecording.VirtualKey);
        _stopHotkeyKey = FindHotkeyKey(settings.Hotkeys.StopRecording.VirtualKey);
        _pauseHotkeyKey = FindHotkeyKey(settings.Hotkeys.PauseResumeRecording.VirtualKey);

        RaiseAllStateProperties();
        RaisePropertyChanged(nameof(SelectedQualityPreset));
        RaisePropertyChanged(nameof(SelectedPostRecordingBehavior));
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

    private async Task DismissOnboardingAsync()
    {
        if (_isOnboardingDismissed)
        {
            return;
        }

        _isOnboardingDismissed = true;
        RaisePropertyChanged(nameof(IsOnboardingVisible));
        await SavePreferencesAsync();
        RefreshCommands();
    }

    private async Task ShowOnboardingAgainAsync()
    {
        if (!_isOnboardingDismissed)
        {
            return;
        }

        _isOnboardingDismissed = false;
        RaisePropertyChanged(nameof(IsOnboardingVisible));
        await SavePreferencesAsync();
        RefreshCommands();
    }

    private async Task OpenRecordingAsync()
    {
        if (SelectedRecentRecording is null)
        {
            return;
        }

        var result = await _fileLauncherService.OpenFileAsync(SelectedRecentRecording.FilePath);
        if (!result.IsSuccess)
        {
            PublishFriendlyStatus(result.Error?.Message ?? "ScreenFast could not open that file.");
        }
    }

    private async Task OpenContainingFolderAsync()
    {
        if (SelectedRecentRecording is null)
        {
            return;
        }

        var result = await _fileLauncherService.OpenContainingFolderAsync(SelectedRecentRecording.FilePath);
        if (!result.IsSuccess)
        {
            PublishFriendlyStatus(result.Error?.Message ?? "ScreenFast could not open that folder.");
        }
    }

    private void CopyPath()
    {
        if (SelectedRecentRecording is null || string.IsNullOrWhiteSpace(SelectedRecentRecording.FilePath))
        {
            return;
        }

        try
        {
            var dataPackage = new DataTransfer.DataPackage();
            dataPackage.SetText(SelectedRecentRecording.FilePath);
            DataTransfer.Clipboard.SetContent(dataPackage);
            PublishFriendlyStatus("Recording path copied to clipboard.");
        }
        catch
        {
            PublishFriendlyStatus("ScreenFast could not copy that path.");
        }
    }

    private async Task RemoveHistoryItemAsync()
    {
        if (SelectedRecentRecording is null)
        {
            return;
        }

        await _historyService.RemoveEntryAsync(SelectedRecentRecording.Id);
        SelectedRecentRecording = null;
        await ReloadHistoryAsync();
    }

    private async Task ClearMissingHistoryAsync()
    {
        await _historyService.ClearMissingAsync();
        SelectedRecentRecording = null;
        await ReloadHistoryAsync();
    }

    private async Task ClearAllHistoryAsync()
    {
        await _historyService.ClearAsync();
        SelectedRecentRecording = null;
        await ReloadHistoryAsync();
    }

    private void PublishFriendlyStatus(string message)
    {
        SetShellMessage(message);
        _orchestrator.PublishUserMessage(message);
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

    private PostRecordingOpenBehaviorOption FindPostRecordingBehavior(PostRecordingOpenBehavior behavior)
    {
        return _postRecordingBehaviors.First(option => option.Behavior == behavior);
    }

    private HotkeyModifierOption FindModifier(HotkeyGesture gesture)
    {
        return _hotkeyModifiers.FirstOrDefault(option => option.Matches(gesture)) ?? _hotkeyModifiers[0];
    }

    private HotkeyKeyOption FindHotkeyKey(int virtualKey)
    {
        return _hotkeyKeys.FirstOrDefault(option => option.VirtualKey == virtualKey) ?? _hotkeyKeys[0];
    }

    private void RaiseAllStateProperties()
    {
        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(TimerText));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(PauseResumeText));
        RaisePropertyChanged(nameof(SourceSummary));
        RaisePropertyChanged(nameof(SourceDetails));
        RaisePropertyChanged(nameof(SourceIdText));
        RaisePropertyChanged(nameof(OutputFolderText));
        RaisePropertyChanged(nameof(ShellMessageText));
        RaisePropertyChanged(nameof(ReadySourceText));
        RaisePropertyChanged(nameof(ReadyOutputFolderText));
        RaisePropertyChanged(nameof(AudioChoicesSummary));
        RaisePropertyChanged(nameof(IncludeSystemAudio));
        RaisePropertyChanged(nameof(IncludeMicrophone));
        RaisePropertyChanged(nameof(IsOnboardingVisible));
        RaisePropertyChanged(nameof(CanSelectSource));
        RaisePropertyChanged(nameof(CanPickOutputFolder));
        RaisePropertyChanged(nameof(CanRecord));
        RaisePropertyChanged(nameof(CanPauseResume));
        RaisePropertyChanged(nameof(CanStop));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectSourceCommand.NotifyCanExecuteChanged();
        PickOutputFolderCommand.NotifyCanExecuteChanged();
        RecordCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ApplyHotkeysCommand.NotifyCanExecuteChanged();
        OpenRecordingCommand.NotifyCanExecuteChanged();
        OpenContainingFolderCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        RemoveHistoryItemCommand.NotifyCanExecuteChanged();
        ClearMissingHistoryCommand.NotifyCanExecuteChanged();
        ClearAllHistoryCommand.NotifyCanExecuteChanged();
        DismissOnboardingCommand.NotifyCanExecuteChanged();
        ShowOnboardingAgainCommand.NotifyCanExecuteChanged();
    }
}

public sealed record VideoQualityPresetOption(VideoQualityPreset Preset, string Label);

public sealed record PostRecordingOpenBehaviorOption(PostRecordingOpenBehavior Behavior, string Label);

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




