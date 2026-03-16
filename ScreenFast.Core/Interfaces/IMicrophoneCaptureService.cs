using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IMicrophoneCaptureService
{
    bool IsSupported { get; }

    Task<OperationResult<IAudioCaptureSession>> StartCaptureAsync(
        Action<AudioChunk> onAudioChunk,
        Action<AppError> onError,
        CancellationToken cancellationToken = default);
}
