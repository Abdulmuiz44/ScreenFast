using ScreenFast.Core.Interfaces;

namespace ScreenFast.Audio.Services;

public sealed class NoOpMicrophoneCaptureService : IMicrophoneCaptureService
{
    public bool IsSupported => false;
}
