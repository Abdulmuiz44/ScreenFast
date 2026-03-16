using Microsoft.UI.Dispatching;
using ScreenFast.App.Commands;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.State;

namespace ScreenFast.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IRecorderOrchestrator _orchestrator;
    private readonly DispatcherQueue? _dispatcherQueue;
    private RecorderStatusSnapshot _snapshot;
    private bool _includeSystemAudio;
    private bool _includeMicrophone;

    public MainWindowViewModel(IRecorderOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _snapshot = orchestrator.Snapshot;
        _includeSystemAudio = _snapshot.IncludeSystemAudio;
        _includeMicrophone = _snapshot.IncludeMicrophone;

        SelectSourceCommand = new AsyncRelayCommand(() => _orchestrator.SelectSourceAsync(), () => CanSelectSource);
        PickOutputFolderCommand = new AsyncRelayCommand(() => _orchestrator.ChooseOutputFolderAsync(), () => CanPickOutputFolder);
        RecordCommand = new AsyncRelayCommand(() => _orchestrator.StartRecordingAsync(), () => CanRecord);
        StopCommand = new AsyncRelayCommand(() => _orchestrator.StopRecordingAsync(), () => CanStop);

        _orchestrator.SnapshotChanged += OnSnapshotChanged;
    }

    public AsyncRelayCommand SelectSourceCommand { get; }

    public AsyncRelayCommand PickOutputFolderCommand { get; }

    public AsyncRelayCommand RecordCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public string StatusText => _snapshot.StatusMessage;

    public string TimerText => _snapshot.TimerText;

    public string StateText => _snapshot.State.ToString();

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

    public bool CanSelectSource => _snapshot.State is RecorderState.Idle or RecorderState.Ready or RecorderState.Error;

    public bool CanPickOutputFolder => _snapshot.State is RecorderState.Idle or RecorderState.Ready or RecorderState.Error;

    public bool CanRecord => _snapshot.State == RecorderState.Ready && !string.IsNullOrWhiteSpace(_snapshot.OutputFolder);

    public bool CanStop => _snapshot.State == RecorderState.Recording;

    public void InitializeWindowHandle(nint windowHandle)
    {
        _orchestrator.SetWindowHandle(windowHandle);
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

    private void ApplySnapshot(RecorderStatusSnapshot snapshot)
    {
        _snapshot = snapshot;
        _includeSystemAudio = snapshot.IncludeSystemAudio;
        _includeMicrophone = snapshot.IncludeMicrophone;

        RaisePropertyChanged(nameof(StatusText));
        RaisePropertyChanged(nameof(TimerText));
        RaisePropertyChanged(nameof(StateText));
        RaisePropertyChanged(nameof(SourceSummary));
        RaisePropertyChanged(nameof(SourceDetails));
        RaisePropertyChanged(nameof(SourceIdText));
        RaisePropertyChanged(nameof(OutputFolderText));
        RaisePropertyChanged(nameof(IncludeSystemAudio));
        RaisePropertyChanged(nameof(IncludeMicrophone));
        RaisePropertyChanged(nameof(CanSelectSource));
        RaisePropertyChanged(nameof(CanPickOutputFolder));
        RaisePropertyChanged(nameof(CanRecord));
        RaisePropertyChanged(nameof(CanStop));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        SelectSourceCommand.NotifyCanExecuteChanged();
        PickOutputFolderCommand.NotifyCanExecuteChanged();
        RecordCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
