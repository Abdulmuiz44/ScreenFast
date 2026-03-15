using ScreenFast.Core.Interfaces;

namespace ScreenFast.Audio.Services;

public sealed class NoOpSystemAudioCaptureService : ISystemAudioCaptureService
{
    public bool IsSupported => false;
}
