using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecorderOrchestrator
{
    event EventHandler<RecorderStatusSnapshot>? SnapshotChanged;

    RecorderStatusSnapshot Snapshot { get; }

    void SetWindowHandle(nint windowHandle);

    void ApplyPersistedSettings(AppSettings settings, CaptureSourceModel? restoredSource, string? startupMessage);

    void UpdateAudioPreferences(bool includeSystemAudio, bool includeMicrophone, PostRecordingOpenBehavior postRecordingOpenBehavior);

    void UpdateQualityPreset(VideoQualityPreset preset);

    void PublishUserMessage(string message);

    Task SelectSourceAsync(CancellationToken cancellationToken = default);

    Task ChooseOutputFolderAsync(CancellationToken cancellationToken = default);

    Task StartRecordingAsync(CancellationToken cancellationToken = default);

    Task PauseRecordingAsync(CancellationToken cancellationToken = default);

    Task ResumeRecordingAsync(CancellationToken cancellationToken = default);

    Task TogglePauseResumeAsync(CancellationToken cancellationToken = default);

    Task StopRecordingAsync(CancellationToken cancellationToken = default);
}
