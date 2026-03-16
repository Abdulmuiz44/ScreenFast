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
    private CancellationTokenSource? _timerCancellationSource;
    private DateTimeOffset _recordingStartedAt;

    public RecorderOrchestrator(
        ICaptureSourcePickerService captureSourcePickerService,
        IOutputFolderPickerService outputFolderPickerService,
        IRecordingEncoderService recordingEncoderService)
    {
        _captureSourcePickerService = captureSourcePickerService;
        _outputFolderPickerService = outputFolderPickerService;
        _recordingEncoderService = recordingEncoderService;
        _recordingEncoderService.RuntimeErrorOccurred += OnRecordingRuntimeErrorOccurred;
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

        ResetTimer();
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
            var nextState = Snapshot.SelectedSource is null ? RecorderState.Idle : RecorderState.Ready;
            _stateMachine.TransitionTo(nextState);
            Publish(Snapshot with
            {
                State = nextState,
                OutputFolder = result.Value,
                StatusMessage = "Output folder selected."
            });

            return;
        }

        if (result.IsSuccess)
        {
            Publish(Snapshot with { StatusMessage = "Output folder selection cancelled." });
            return;
        }

        Publish(Snapshot with
        {
            StatusMessage = result.Error?.Message ?? "Output folder selection is unavailable."
        });
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State == RecorderState.Recording)
        {
            Publish(Snapshot with { StatusMessage = "Recording is already in progress." });
            return;
        }

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
            StatusMessage = "Starting recording...",
            TimerText = "00:00:00"
        });

        var result = await _recordingEncoderService.StartAsync(
            new RecordingStartRequest(
                Snapshot.SelectedSource,
                Snapshot.OutputFolder,
                Snapshot.IncludeSystemAudio,
                Snapshot.IncludeMicrophone),
            cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            _recordingStartedAt = DateTimeOffset.UtcNow;
            StartTimer();
            var audioSummary = BuildAudioSummary(result.Value);
            var status = string.IsNullOrWhiteSpace(result.Value.WarningMessage)
                ? $"Recording to {Path.GetFileName(result.Value.FilePath)}{audioSummary}"
                : $"Recording to {Path.GetFileName(result.Value.FilePath)}{audioSummary}. {result.Value.WarningMessage}";

            Publish(Snapshot with
            {
                State = RecorderState.Recording,
                StatusMessage = status
            });
            return;
        }

        ResetTimer();
        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = result.Error?.Message ?? "Recording failed to start."
        });
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State != RecorderState.Recording)
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

        var stopResult = await _recordingEncoderService.StopAsync(cancellationToken);
        ResetTimer();

        if (!stopResult.IsSuccess || string.IsNullOrWhiteSpace(stopResult.Value))
        {
            _stateMachine.TransitionTo(RecorderState.Error);
            Publish(Snapshot with
            {
                State = RecorderState.Error,
                StatusMessage = stopResult.Error?.Message ?? "ScreenFast could not finalize the recording."
            });
            return;
        }

        var nextState = Snapshot.SelectedSource is not null && !string.IsNullOrWhiteSpace(Snapshot.OutputFolder)
            ? RecorderState.Ready
            : RecorderState.Idle;

        _stateMachine.TransitionTo(nextState);
        Publish(Snapshot with
        {
            State = nextState,
            StatusMessage = $"Saved MP4 to {stopResult.Value}",
            TimerText = "00:00:00"
        });
    }

    private void OnRecordingRuntimeErrorOccurred(object? sender, AppError error)
    {
        ResetTimer();
        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = error.Message,
            TimerText = "00:00:00"
        });
    }

    private void StartTimer()
    {
        ResetTimer();
        _timerCancellationSource = new CancellationTokenSource();
        _ = RunTimerLoopAsync(_timerCancellationSource.Token);
    }

    private async Task RunTimerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var elapsed = DateTimeOffset.UtcNow - _recordingStartedAt;
                Publish(Snapshot with { TimerText = elapsed.ToString(@"hh\:mm\:ss") });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ResetTimer()
    {
        if (_timerCancellationSource is not null)
        {
            _timerCancellationSource.Cancel();
            _timerCancellationSource.Dispose();
            _timerCancellationSource = null;
        }

        Publish(Snapshot with { TimerText = "00:00:00" });
    }

    private void PublishError(AppError error)
    {
        ResetTimer();
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

    private static string BuildAudioSummary(RecordingSessionInfo sessionInfo)
    {
        if (sessionInfo.IncludesSystemAudio && sessionInfo.IncludesMicrophone)
        {
            return " with system audio and microphone";
        }

        if (sessionInfo.IncludesSystemAudio)
        {
            return " with system audio";
        }

        if (sessionInfo.IncludesMicrophone)
        {
            return " with microphone";
        }

        return string.Empty;
    }
}
