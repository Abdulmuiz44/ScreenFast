using NAudio.Wave;

namespace ScreenFast.Audio.Internal;

internal sealed class StereoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _readBuffer;

    public StereoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _readBuffer = new float[4096 * Math.Max(1, source.WaveFormat.Channels)];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _source.WaveFormat.Channels;
        var framesRequested = count / 2;
        var samplesNeeded = framesRequested * channels;
        EnsureCapacity(samplesNeeded);

        var samplesRead = _source.Read(_readBuffer, 0, samplesNeeded);
        var framesRead = samplesRead / channels;

        for (var frameIndex = 0; frameIndex < framesRead; frameIndex++)
        {
            var sourceBase = frameIndex * channels;
            buffer[offset + (frameIndex * 2)] = _readBuffer[sourceBase];
            buffer[offset + (frameIndex * 2) + 1] = channels > 1 ? _readBuffer[sourceBase + 1] : _readBuffer[sourceBase];
        }

        return framesRead * 2;
    }

    private void EnsureCapacity(int requiredSamples)
    {
        if (requiredSamples <= _readBuffer.Length)
        {
            return;
        }

        Array.Resize(ref _readBuffer, requiredSamples);
    }
}
