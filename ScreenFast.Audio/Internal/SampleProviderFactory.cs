using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ScreenFast.Audio.Internal;

internal static class SampleProviderFactory
{
    public static ISampleProvider CreateStandardProvider(BufferedWaveProvider bufferedWaveProvider)
    {
        ISampleProvider sampleProvider = bufferedWaveProvider.ToSampleProvider();

        if (sampleProvider.WaveFormat.Channels == 1)
        {
            sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
        }
        else if (sampleProvider.WaveFormat.Channels != AudioFormatConstants.TargetChannels)
        {
            sampleProvider = new StereoDownmixSampleProvider(sampleProvider);
        }

        if (sampleProvider.WaveFormat.SampleRate != AudioFormatConstants.TargetSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, AudioFormatConstants.TargetSampleRate);
        }

        return sampleProvider;
    }
}
