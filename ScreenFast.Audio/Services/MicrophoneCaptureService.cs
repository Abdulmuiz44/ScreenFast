using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Audio.Services;

public sealed class MicrophoneCaptureService : WasapiAudioCaptureServiceBase, IMicrophoneCaptureService
{
    public MicrophoneCaptureService(IScreenFastLogService logService)
        : base(logService)
    {
    }

    public bool IsSupported => true;

    public Task<OperationResult<IAudioCaptureSession>> StartCaptureAsync(
        Action<AudioChunk> onAudioChunk,
        Action<AppError> onError,
        CancellationToken cancellationToken = default)
    {
        return StartAsync(
            AudioInputKind.Microphone,
            device => new WasapiCapture(device),
            DataFlow.Capture,
            onAudioChunk,
            onError,
            cancellationToken);
    }
}
