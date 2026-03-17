using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface ICaptureSession : IAsyncDisposable
{
    nint NativeDevicePointer { get; }

    int Width { get; }

    int Height { get; }

    OperationResult Start();

    OperationResult Pause();

    OperationResult Resume();

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
}
