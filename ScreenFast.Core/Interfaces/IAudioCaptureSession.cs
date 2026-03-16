using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IAudioCaptureSession : IAsyncDisposable
{
    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
}
