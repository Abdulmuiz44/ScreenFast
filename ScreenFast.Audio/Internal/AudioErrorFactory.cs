using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Audio.Internal;

internal static class AudioErrorFactory
{
    public static AppError DeviceUnavailable(AudioInputKind kind, string detail)
    {
        var label = kind == AudioInputKind.SystemAudio ? "system audio" : "microphone";
        return AppError.AudioCaptureFailed($"ScreenFast could not start {label}: {detail}");
    }

    public static AppError RuntimeFailure(AudioInputKind kind, string detail)
    {
        var label = kind == AudioInputKind.SystemAudio ? "system audio" : "microphone";
        return AppError.AudioCaptureFailed($"ScreenFast lost {label} while recording: {detail}");
    }
}
