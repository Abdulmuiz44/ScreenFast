using NAudio.Wave;

namespace ScreenFast.Audio.Internal;

internal static class AudioFormatConstants
{
    public static readonly WaveFormat TargetFloatFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    public const int PumpMilliseconds = 20;
    public const int TargetSampleRate = 48_000;
    public const int TargetChannels = 2;
}
