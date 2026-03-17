using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Audio.Services;

public sealed class NoOpMicrophoneCaptureService : IMicrophoneCaptureService
{
    public bool IsSupported => false;

    public Task<OperationResult<IAudioCaptureSession>> StartCaptureAsync(
        Action<AudioChunk> onAudioChunk,
        Action<AppError> onError,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            OperationResult<IAudioCaptureSession>.Failure(
                AppError.AudioCaptureFailed("Microphone capture is not available in the no-op implementation.")));
    }
}
