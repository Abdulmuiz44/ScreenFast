using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecorderOrchestrator
{
    event EventHandler<RecorderStatusSnapshot>? SnapshotChanged;

    RecorderStatusSnapshot Snapshot { get; }

    void SetWindowHandle(nint windowHandle);

    void UpdateAudioPreferences(bool includeSystemAudio, bool includeMicrophone);

    Task SelectSourceAsync(CancellationToken cancellationToken = default);

    Task ChooseOutputFolderAsync(CancellationToken cancellationToken = default);

    Task StartRecordingAsync(CancellationToken cancellationToken = default);

    Task PauseRecordingAsync(CancellationToken cancellationToken = default);

    Task ResumeRecordingAsync(CancellationToken cancellationToken = default);

    Task TogglePauseResumeAsync(CancellationToken cancellationToken = default);

    Task StopRecordingAsync(CancellationToken cancellationToken = default);
}
