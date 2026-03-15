using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;

namespace ScreenFast.App.Services;

public sealed class RecorderOrchestrator : IRecorderOrchestrator
{
    private readonly ICaptureSourcePickerService _captureSourcePickerService;
    private readonly IOutputFolderPickerService _outputFolderPickerService;
    private readonly IRecordingEncoderService _recordingEncoderService;
    private readonly RecorderStateMachine _stateMachine = new();
    private nint _windowHandle;

    public RecorderOrchestrator(
        ICaptureSourcePickerService captureSourcePickerService,
        IOutputFolderPickerService outputFolderPickerService,
        IRecordingEncoderService recordingEncoderService)
    {
        _captureSourcePickerService = captureSourcePickerService;
        _outputFolderPickerService = outputFolderPickerService;
        _recordingEncoderService = recordingEncoderService;
        Snapshot = RecorderStatusSnapshot.CreateDefault();
    }

    public event EventHandler<RecorderStatusSnapshot>? SnapshotChanged;

    public RecorderStatusSnapshot Snapshot { get; private set; }

    public void SetWindowHandle(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public void UpdateAudioPreferences(bool includeSystemAudio, bool includeMicrophone)
    {
        Publish(Snapshot with
        {
            IncludeSystemAudio = includeSystemAudio,
            IncludeMicrophone = includeMicrophone
        });
    }

    public async Task SelectSourceAsync(CancellationToken cancellationToken = default)
    {
        var originalSource = Snapshot.SelectedSource;
        var transition = _stateMachine.TransitionTo(RecorderState.Selecting);
        if (!transition.IsSuccess)
        {
            PublishError(transition.Error!);
            return;
        }

        Publish(Snapshot with
        {
            State = RecorderState.Selecting,
            StatusMessage = "Select a display or app window."
        });

        var selectionResult = await _captureSourcePickerService.PickAsync(_windowHandle, cancellationToken);
        if (selectionResult.IsSuccess && selectionResult.Source is not null)
        {
            _stateMachine.TransitionTo(RecorderState.Ready);
            Publish(Snapshot with
            {
                State = RecorderState.Ready,
                SelectedSource = selectionResult.Source,
                StatusMessage = $"Ready: {selectionResult.Source.TypeDisplayName} selected."
            });

            return;
        }

        if (selectionResult.Status == CaptureSourceSelectionStatus.Cancelled)
        {
            var nextState = originalSource is null ? RecorderState.Idle : RecorderState.Ready;
            _stateMachine.TransitionTo(nextState);
            Publish(Snapshot with
            {
                State = nextState,
                SelectedSource = originalSource,
                StatusMessage = originalSource is null
                    ? "Source selection cancelled."
                    : "Source selection cancelled. Keeping the previous source."
            });

            return;
        }

        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            SelectedSource = originalSource,
            StatusMessage = selectionResult.Error?.Message ?? "Source selection failed."
        });
    }

    public async Task ChooseOutputFolderAsync(CancellationToken cancellationToken = default)
    {
        if (_windowHandle == nint.Zero)
        {
            PublishError(AppError.MissingWindowHandle());
            return;
        }

        var result = await _outputFolderPickerService.PickOutputFolderAsync(_windowHandle, cancellationToken);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
        {
            Publish(Snapshot with
            {
                OutputFolder = result.Value,
                StatusMessage = "Output folder selected."
            });

            return;
        }

        Publish(Snapshot with
        {
            StatusMessage = result.Error?.Message ?? "Output folder selection is unavailable."
        });
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedSource is null)
        {
            Publish(Snapshot with { StatusMessage = "Select a source before recording." });
            return;
        }

        if (string.IsNullOrWhiteSpace(Snapshot.OutputFolder))
        {
            Publish(Snapshot with { StatusMessage = "Choose an output folder before recording." });
            return;
        }

        var transition = _stateMachine.TransitionTo(RecorderState.Recording);
        if (!transition.IsSuccess)
        {
            PublishError(transition.Error!);
            return;
        }

        Publish(Snapshot with
        {
            State = RecorderState.Recording,
            StatusMessage = "Starting recording..."
        });

        var result = await _recordingEncoderService.StartAsync(
            new RecordingStartRequest(
                Snapshot.SelectedSource,
                Snapshot.OutputFolder,
                Snapshot.IncludeSystemAudio,
                Snapshot.IncludeMicrophone),
            cancellationToken);

        if (result.IsSuccess)
        {
            Publish(Snapshot with { StatusMessage = "Recording started." });
            return;
        }

        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = result.Error?.Message ?? "Recording failed to start."
        });
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State is not (RecorderState.Recording or RecorderState.Stopping))
        {
            Publish(Snapshot with { StatusMessage = "There is no active recording to stop." });
            return;
        }

        var transition = _stateMachine.TransitionTo(RecorderState.Stopping);
        if (!transition.IsSuccess)
        {
            PublishError(transition.Error!);
            return;
        }

        Publish(Snapshot with
        {
            State = RecorderState.Stopping,
            StatusMessage = "Stopping recording..."
        });

        await _recordingEncoderService.StopAsync(cancellationToken);
        _stateMachine.TransitionTo(RecorderState.Idle);

        Publish(Snapshot with
        {
            State = RecorderState.Idle,
            StatusMessage = "Recording stopped.",
            SelectedSource = null,
            TimerText = "00:00:00"
        });
    }

    private void PublishError(AppError error)
    {
        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = error.Message
        });
    }

    private void Publish(RecorderStatusSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
