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
    private readonly IRecordingHistoryService _recordingHistoryService;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IRecordingPreflightValidator _recordingPreflightValidator;
    private readonly IRecoveryService _recoveryService;
    private readonly IScreenFastLogService _logService;
    private readonly RecorderStateMachine _stateMachine = new();

    private nint _windowHandle;
    private CancellationTokenSource? _timerCancellationSource;
    private DateTimeOffset _recordingStartedAt;
    private TimeSpan _elapsedBeforePause;
    private RecordingSessionInfo? _activeSession;
    private string? _activeSessionId;

    public RecorderOrchestrator(
        ICaptureSourcePickerService captureSourcePickerService,
        IOutputFolderPickerService outputFolderPickerService,
        IRecordingEncoderService recordingEncoderService,
        IRecordingHistoryService recordingHistoryService,
        IFileLauncherService fileLauncherService,
        IRecordingPreflightValidator recordingPreflightValidator,
        IRecoveryService recoveryService,
        IScreenFastLogService logService)
    {
        _captureSourcePickerService = captureSourcePickerService;
        _outputFolderPickerService = outputFolderPickerService;
        _recordingEncoderService = recordingEncoderService;
        _recordingHistoryService = recordingHistoryService;
        _fileLauncherService = fileLauncherService;
        _recordingPreflightValidator = recordingPreflightValidator;
        _recoveryService = recoveryService;
        _logService = logService;
        _recordingEncoderService.RuntimeErrorOccurred += OnRecordingRuntimeErrorOccurred;
        Snapshot = RecorderStatusSnapshot.CreateDefault();
    }

    public event EventHandler<RecorderStatusSnapshot>? SnapshotChanged;

    public RecorderStatusSnapshot Snapshot { get; private set; }

    public void SetWindowHandle(nint windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public void ApplyPersistedSettings(AppSettings settings, CaptureSourceModel? restoredSource, string? startupMessage)
    {
        var nextState = restoredSource is not null ? RecorderState.Ready : RecorderState.Idle;
        _stateMachine.TransitionTo(nextState);
        Publish(Snapshot with
        {
            State = nextState,
            SelectedSource = restoredSource,
            OutputFolder = settings.OutputFolder,
            IncludeSystemAudio = settings.IncludeSystemAudio,
            IncludeMicrophone = settings.IncludeMicrophone,
            QualityPreset = settings.QualityPreset,
            PostRecordingOpenBehavior = settings.PostRecordingOpenBehavior,
            StatusMessage = string.IsNullOrWhiteSpace(startupMessage)
                ? nextState == RecorderState.Ready
                    ? "Ready to record."
                    : "Choose a display or window to get ready."
                : startupMessage
        });
        _logService.Info("recorder.persisted_settings_applied", "ScreenFast applied persisted recorder settings.", BuildSnapshotProperties());
    }

    public void UpdateAudioPreferences(bool includeSystemAudio, bool includeMicrophone, PostRecordingOpenBehavior postRecordingOpenBehavior)
    {
        Publish(Snapshot with
        {
            IncludeSystemAudio = includeSystemAudio,
            IncludeMicrophone = includeMicrophone,
            PostRecordingOpenBehavior = postRecordingOpenBehavior
        });
    }

    public void UpdateQualityPreset(VideoQualityPreset preset)
    {
        Publish(Snapshot with { QualityPreset = preset });
    }

    public void PublishUserMessage(string message)
    {
        Publish(Snapshot with { StatusMessage = message });
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

        _logService.Info("source.selection_started", "ScreenFast started source selection.");
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
            _logService.Info(
                "source.selection_succeeded",
                "ScreenFast selected a capture source.",
                new Dictionary<string, object?>
                {
                    ["sourceSummary"] = $"{selectionResult.Source.TypeDisplayName}: {selectionResult.Source.DisplayName}",
                    ["sourceId"] = selectionResult.Source.SourceId,
                    ["dimensions"] = selectionResult.Source.DimensionsText
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
            _logService.Info("source.selection_cancelled", "ScreenFast source selection was cancelled.");
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
        _logService.Warning("source.selection_failed", selectionResult.Error?.Message ?? "Source selection failed.");
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
            _logService.Info("folder.selection_succeeded", "ScreenFast selected an output folder.", new Dictionary<string, object?> { ["outputFolder"] = result.Value });
            return;
        }

        if (result.IsSuccess)
        {
            Publish(Snapshot with { StatusMessage = "Output folder selection cancelled." });
            _logService.Info("folder.selection_cancelled", "Output folder selection was cancelled.");
            return;
        }

        Publish(Snapshot with
        {
            StatusMessage = result.Error?.Message ?? "Output folder selection is unavailable."
        });
        _logService.Warning("folder.selection_failed", result.Error?.Message ?? "Output folder selection failed.");
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State is RecorderState.Recording or RecorderState.Paused)
        {
            Publish(Snapshot with { StatusMessage = "A recording session is already active." });
            return;
        }

        _logService.Info("recording.start_requested", "ScreenFast received a start recording request.", BuildSnapshotProperties());
        var preflightResult = await _recordingPreflightValidator.ValidateAsync(Snapshot, cancellationToken);
        if (!preflightResult.IsSuccess)
        {
            Publish(Snapshot with { StatusMessage = preflightResult.Error?.Message ?? "ScreenFast is not ready to record yet." });
            _logService.Warning("recording.preflight_failed", preflightResult.Error?.Message ?? "Preflight failed.");
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
                Snapshot.SelectedSource!,
                Snapshot.OutputFolder!,
                Snapshot.IncludeSystemAudio,
                Snapshot.IncludeMicrophone,
                Snapshot.QualityPreset),
            cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            _activeSession = result.Value;
            _activeSessionId = Guid.NewGuid().ToString("N");
            _elapsedBeforePause = TimeSpan.Zero;
            _recordingStartedAt = DateTimeOffset.UtcNow;
            await _recoveryService.MarkSessionStartedAsync(
                new RecoverySessionMarker(
                    _activeSessionId,
                    _recordingStartedAt,
                    BuildSourceSummary(),
                    result.Value.FilePath,
                    Snapshot.QualityPreset,
                    Snapshot.IncludeSystemAudio,
                    Snapshot.IncludeMicrophone),
                cancellationToken);
            StartTimerLoop();
            var audioSummary = BuildAudioSummary(result.Value);
            var qualitySummary = VideoQualityPresets.Get(result.Value.QualityPreset).DisplayName;
            var status = string.IsNullOrWhiteSpace(result.Value.WarningMessage)
                ? $"Recording to {Path.GetFileName(result.Value.FilePath)} using {qualitySummary}{audioSummary}"
                : $"Recording to {Path.GetFileName(result.Value.FilePath)} using {qualitySummary}{audioSummary}. {result.Value.WarningMessage}";

            Publish(Snapshot with
            {
                State = RecorderState.Recording,
                StatusMessage = status,
                TimerText = FormatElapsed(GetElapsed())
            });
            _logService.Info(
                "recording.started",
                "ScreenFast started recording successfully.",
                BuildRecordingProperties(result.Value.FilePath, TimeSpan.Zero));
            return;
        }

        _activeSession = null;
        _activeSessionId = null;
        ResetTimer();
        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = result.Error?.Message ?? "Recording failed to start."
        });
        _logService.Warning("recording.start_failed", result.Error?.Message ?? "Recording failed to start.");
    }

    public async Task PauseRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State == RecorderState.Paused)
        {
            Publish(Snapshot with { StatusMessage = "Recording is already paused." });
            return;
        }

        if (Snapshot.State != RecorderState.Recording)
        {
            Publish(Snapshot with { StatusMessage = "Recording must be running before it can be paused." });
            return;
        }

        var transition = _stateMachine.TransitionTo(RecorderState.Paused);
        if (!transition.IsSuccess)
        {
            PublishError(transition.Error!);
            return;
        }

        var pauseResult = await _recordingEncoderService.PauseAsync(cancellationToken);
        if (!pauseResult.IsSuccess)
        {
            _stateMachine.TransitionTo(RecorderState.Recording);
            Publish(Snapshot with
            {
                State = RecorderState.Recording,
                StatusMessage = pauseResult.Error?.Message ?? "ScreenFast could not pause the recording."
            });
            _logService.Warning("recording.pause_failed", pauseResult.Error?.Message ?? "Pause failed.");
            return;
        }

        _elapsedBeforePause = GetElapsed();
        StopTimerLoop(resetDisplay: false);
        Publish(Snapshot with
        {
            State = RecorderState.Paused,
            StatusMessage = "Recording paused.",
            TimerText = FormatElapsed(_elapsedBeforePause)
        });
        _logService.Info("recording.paused", "ScreenFast paused the recording.", BuildRecordingProperties(_activeSession?.FilePath, _elapsedBeforePause));
    }

    public async Task ResumeRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State == RecorderState.Recording)
        {
            Publish(Snapshot with { StatusMessage = "Recording is already running." });
            return;
        }

        if (Snapshot.State != RecorderState.Paused)
        {
            Publish(Snapshot with { StatusMessage = "Recording must be paused before it can resume." });
            return;
        }

        var transition = _stateMachine.TransitionTo(RecorderState.Recording);
        if (!transition.IsSuccess)
        {
            PublishError(transition.Error!);
            return;
        }

        var resumeResult = await _recordingEncoderService.ResumeAsync(cancellationToken);
        if (!resumeResult.IsSuccess)
        {
            _stateMachine.TransitionTo(RecorderState.Paused);
            Publish(Snapshot with
            {
                State = RecorderState.Paused,
                StatusMessage = resumeResult.Error?.Message ?? "ScreenFast could not resume the recording."
            });
            _logService.Warning("recording.resume_failed", resumeResult.Error?.Message ?? "Resume failed.");
            return;
        }

        _recordingStartedAt = DateTimeOffset.UtcNow;
        StartTimerLoop();
        Publish(Snapshot with
        {
            State = RecorderState.Recording,
            StatusMessage = "Recording resumed.",
            TimerText = FormatElapsed(GetElapsed())
        });
        _logService.Info("recording.resumed", "ScreenFast resumed the recording.", BuildRecordingProperties(_activeSession?.FilePath, GetElapsed()));
    }

    public Task TogglePauseResumeAsync(CancellationToken cancellationToken = default)
    {
        return Snapshot.State switch
        {
            RecorderState.Recording => PauseRecordingAsync(cancellationToken),
            RecorderState.Paused => ResumeRecordingAsync(cancellationToken),
            _ => HandleInvalidPauseToggleAsync()
        };
    }

    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.State is not RecorderState.Recording and not RecorderState.Paused)
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

        var duration = GetElapsed();
        StopTimerLoop(resetDisplay: false);
        Publish(Snapshot with
        {
            State = RecorderState.Stopping,
            StatusMessage = "Stopping recording..."
        });
        _logService.Info("recording.stop_requested", "ScreenFast is stopping the recording.", BuildRecordingProperties(_activeSession?.FilePath, duration));

        var stopResult = await _recordingEncoderService.StopAsync(cancellationToken);
        ResetTimer();
        await _recoveryService.ClearActiveSessionAsync(cancellationToken);

        if (!stopResult.IsSuccess || string.IsNullOrWhiteSpace(stopResult.Value))
        {
            await TryAddHistoryEntryAsync(CreateFailedEntry(duration, stopResult.Error?.Message));
            _stateMachine.TransitionTo(RecorderState.Error);
            Publish(Snapshot with
            {
                State = RecorderState.Error,
                StatusMessage = stopResult.Error?.Message ?? "ScreenFast could not finalize the recording."
            });
            _logService.Warning("recording.stop_failed", stopResult.Error?.Message ?? "ScreenFast could not finalize the recording.", BuildRecordingProperties(_activeSession?.FilePath, duration));
            _activeSession = null;
            _activeSessionId = null;
            return;
        }

        var path = stopResult.Value;
        var fileName = Path.GetFileName(path);

        await TryAddHistoryEntryAsync(CreateSuccessEntry(path, fileName, duration));

        var nextState = Snapshot.SelectedSource is not null && !string.IsNullOrWhiteSpace(Snapshot.OutputFolder)
            ? RecorderState.Ready
            : RecorderState.Idle;

        _stateMachine.TransitionTo(nextState);
        Publish(Snapshot with
        {
            State = nextState,
            StatusMessage = $"Saved MP4 to {path}",
            TimerText = "00:00:00"
        });

        _logService.Info("recording.stopped", "ScreenFast finalized the recording successfully.", BuildRecordingProperties(path, duration));
        _activeSession = null;
        _activeSessionId = null;
        await TryRunPostRecordingActionAsync(path);
    }

    private async Task TryRunPostRecordingActionAsync(string filePath)
    {
        OperationResult result;
        if (Snapshot.PostRecordingOpenBehavior == PostRecordingOpenBehavior.OpenFile)
        {
            result = await _fileLauncherService.OpenFileAsync(filePath);
        }
        else if (Snapshot.PostRecordingOpenBehavior == PostRecordingOpenBehavior.OpenContainingFolder)
        {
            result = await _fileLauncherService.OpenContainingFolderAsync(filePath);
        }
        else
        {
            return;
        }

        if (!result.IsSuccess)
        {
            Publish(Snapshot with
            {
                StatusMessage = result.Error?.Message ?? "ScreenFast could not run the post-recording action."
            });
            _logService.Warning("recording.post_action_failed", result.Error?.Message ?? "Post-recording action failed.", new Dictionary<string, object?> { ["path"] = filePath });
        }
        else
        {
            _logService.Info("recording.post_action_completed", "ScreenFast ran the post-recording action.", new Dictionary<string, object?> { ["path"] = filePath, ["behavior"] = Snapshot.PostRecordingOpenBehavior });
        }
    }

    private RecordingHistoryEntry CreateSuccessEntry(string filePath, string fileName, TimeSpan duration)
    {
        long? size = null;
        var exists = false;

        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists)
            {
                exists = true;
                size = info.Length;
            }
        }
        catch
        {
        }

        return new RecordingHistoryEntry(
            Guid.NewGuid(),
            filePath,
            fileName,
            DateTimeOffset.UtcNow,
            duration,
            BuildSourceSummary(),
            Snapshot.IncludeSystemAudio,
            Snapshot.IncludeMicrophone,
            VideoQualityPresets.Get(Snapshot.QualityPreset).DisplayName,
            true,
            null,
            size,
            exists);
    }

    private RecordingHistoryEntry CreateFailedEntry(TimeSpan duration, string? failureSummary)
    {
        var maybePath = _activeSession?.FilePath ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(maybePath) ? "(failed recording)" : Path.GetFileName(maybePath);
        long? size = null;
        var exists = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(maybePath))
            {
                var info = new FileInfo(maybePath);
                if (info.Exists)
                {
                    exists = true;
                    size = info.Length;
                }
            }
        }
        catch
        {
        }

        return new RecordingHistoryEntry(
            Guid.NewGuid(),
            maybePath,
            fileName,
            DateTimeOffset.UtcNow,
            duration,
            BuildSourceSummary(),
            Snapshot.IncludeSystemAudio,
            Snapshot.IncludeMicrophone,
            VideoQualityPresets.Get(Snapshot.QualityPreset).DisplayName,
            false,
            string.IsNullOrWhiteSpace(failureSummary) ? "Recording failed." : failureSummary,
            size,
            exists);
    }

    private string BuildSourceSummary()
    {
        return Snapshot.SelectedSource is null
            ? "No source"
            : $"{Snapshot.SelectedSource.TypeDisplayName}: {Snapshot.SelectedSource.DisplayName}";
    }

    private async Task TryAddHistoryEntryAsync(RecordingHistoryEntry entry)
    {
        try
        {
            await _recordingHistoryService.AddEntryAsync(entry);
        }
        catch
        {
            _logService.Warning("history.entry_add_failed", "Recording finished, but ScreenFast could not update history.");
        }
    }

    private Task HandleInvalidPauseToggleAsync()
    {
        Publish(Snapshot with
        {
            StatusMessage = Snapshot.State == RecorderState.Ready
                ? "Start recording before using pause or resume."
                : "Pause and resume are only available while recording."
        });

        return Task.CompletedTask;
    }

    private async void OnRecordingRuntimeErrorOccurred(object? sender, AppError error)
    {
        var duration = GetElapsed();
        var failedPath = _activeSession?.FilePath;
        if (Snapshot.State is not RecorderState.Recording and not RecorderState.Paused and not RecorderState.Stopping)
        {
            Publish(Snapshot with
            {
                State = RecorderState.Error,
                StatusMessage = error.Message,
                TimerText = "00:00:00"
            });
            _logService.Warning("recording.runtime_error", error.Message);
            return;
        }

        StopTimerLoop(resetDisplay: false);

        try
        {
            await _recordingEncoderService.StopAsync();
        }
        catch
        {
        }

        await _recoveryService.ClearActiveSessionAsync();
        await TryAddHistoryEntryAsync(CreateFailedEntry(duration, error.Message));
        _activeSession = null;
        _activeSessionId = null;
        ResetTimer();
        _stateMachine.TransitionTo(RecorderState.Error);
        Publish(Snapshot with
        {
            State = RecorderState.Error,
            StatusMessage = error.Message,
            TimerText = "00:00:00"
        });
        _logService.Warning("recording.runtime_error", error.Message, BuildRecordingProperties(failedPath, duration));
    }

    private void StartTimerLoop()
    {
        StopTimerLoop(resetDisplay: false);
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
                Publish(Snapshot with { TimerText = FormatElapsed(GetElapsed()) });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private TimeSpan GetElapsed()
    {
        return Snapshot.State == RecorderState.Recording
            ? _elapsedBeforePause + (DateTimeOffset.UtcNow - _recordingStartedAt)
            : _elapsedBeforePause;
    }

    private void StopTimerLoop(bool resetDisplay)
    {
        if (_timerCancellationSource is not null)
        {
            _timerCancellationSource.Cancel();
            _timerCancellationSource.Dispose();
            _timerCancellationSource = null;
        }

        if (resetDisplay)
        {
            Publish(Snapshot with { TimerText = "00:00:00" });
        }
    }

    private void ResetTimer()
    {
        StopTimerLoop(resetDisplay: false);
        _elapsedBeforePause = TimeSpan.Zero;
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
        _logService.Warning("recorder.error", error.Message);
    }

    private void Publish(RecorderStatusSnapshot snapshot)
    {
        Snapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.ToString(@"hh\:mm\:ss");
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

    private Dictionary<string, object?> BuildSnapshotProperties()
    {
        return new Dictionary<string, object?>
        {
            ["state"] = Snapshot.State,
            ["sourceSummary"] = BuildSourceSummary(),
            ["qualityPreset"] = Snapshot.QualityPreset,
            ["includeSystemAudio"] = Snapshot.IncludeSystemAudio,
            ["includeMicrophone"] = Snapshot.IncludeMicrophone,
            ["outputFolder"] = Snapshot.OutputFolder
        };
    }

    private Dictionary<string, object?> BuildRecordingProperties(string? filePath, TimeSpan duration)
    {
        var properties = BuildSnapshotProperties();
        properties["sessionId"] = _activeSessionId;
        properties["filePath"] = filePath;
        properties["duration"] = duration;
        return properties;
    }
}
