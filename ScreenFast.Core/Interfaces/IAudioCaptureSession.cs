using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IAudioCaptureSession : IAsyncDisposable
{
    OperationResult Pause();

    OperationResult Resume();

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
}
