
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Encoding.Interop;

namespace ScreenFast.Encoding.Services;

public sealed class MediaFoundationRecordingEncoderService : IRecordingEncoderService, IDisposable
{
    private readonly ICaptureSessionFactory _captureSessionFactory;
    private readonly ISystemAudioCaptureService _systemAudioCaptureService;
    private readonly IMicrophoneCaptureService _microphoneCaptureService;
    private readonly object _sync = new();
    private readonly MediaFoundationLifetime _lifetime = new();

    private ICaptureSession? _captureSession;
    private IAudioCaptureSession? _systemAudioSession;
    private IAudioCaptureSession? _microphoneSession;
    private MediaFoundationMp4Writer? _writer;
    private AudioMixerPump? _audioMixer;
    private string? _outputPath;
    private AppError? _backgroundFailure;
    private int _runtimeFaulted;
    private bool _isRecording;
    private bool _isPaused;
    private long? _pauseStartedTimestamp;
    private long _totalPausedDurationHundredsOfNanoseconds;

    public MediaFoundationRecordingEncoderService(
        ICaptureSessionFactory captureSessionFactory,
        ISystemAudioCaptureService systemAudioCaptureService,
        IMicrophoneCaptureService microphoneCaptureService)
    {
        _captureSessionFactory = captureSessionFactory;
        _systemAudioCaptureService = systemAudioCaptureService;
        _microphoneCaptureService = microphoneCaptureService;
    }

    public event EventHandler<AppError>? RuntimeErrorOccurred;

    public async Task<OperationResult<RecordingSessionInfo>> StartAsync(RecordingStartRequest request, CancellationToken cancellationToken = default)
    {
        if (_isRecording)
        {
            return OperationResult<RecordingSessionInfo>.Failure(AppError.InvalidState("Recording is already in progress."));
        }

        if (!Directory.Exists(request.OutputFolder))
        {
            return OperationResult<RecordingSessionInfo>.Failure(
                AppError.RecordingFailed("The selected output folder does not exist anymore. Choose it again before recording."));
        }

        _backgroundFailure = null;
        _runtimeFaulted = 0;
        _isPaused = false;
        _pauseStartedTimestamp = null;
        _totalPausedDurationHundredsOfNanoseconds = 0;

        var captureSessionResult = await _captureSessionFactory.CreateAsync(request.Source, ProcessVideoFrame, HandleRuntimeFailure, cancellationToken);
        if (!captureSessionResult.IsSuccess || captureSessionResult.Value is null)
        {
            return OperationResult<RecordingSessionInfo>.Failure(captureSessionResult.Error!);
        }

        _captureSession = captureSessionResult.Value;
        _outputPath = BuildOutputPath(request.OutputFolder);

        var includeSystemAudio = request.IncludeSystemAudio;
        var includeMicrophone = request.IncludeMicrophone;
        var warnings = new List<string>();

        try
        {
            _audioMixer = request.IncludeSystemAudio || request.IncludeMicrophone ? new AudioMixerPump() : null;

            if (request.IncludeSystemAudio && _audioMixer is not null)
            {
                var systemAudioResult = await _systemAudioCaptureService.StartCaptureAsync(OnAudioChunk, HandleRuntimeFailure, cancellationToken);
                if (systemAudioResult.IsSuccess && systemAudioResult.Value is not null)
                {
                    _systemAudioSession = systemAudioResult.Value;
                }
                else
                {
                    includeSystemAudio = false;
                    warnings.Add(systemAudioResult.Error?.Message ?? "System audio was requested but could not be started.");
                }
            }

            if (request.IncludeMicrophone && _audioMixer is not null)
            {
                var microphoneResult = await _microphoneCaptureService.StartCaptureAsync(OnAudioChunk, HandleRuntimeFailure, cancellationToken);
                if (microphoneResult.IsSuccess && microphoneResult.Value is not null)
                {
                    _microphoneSession = microphoneResult.Value;
                }
                else
                {
                    includeMicrophone = false;
                    warnings.Add(microphoneResult.Error?.Message ?? "Microphone was requested but could not be started.");
                }
            }

            var includeAudioStream = includeSystemAudio || includeMicrophone;
            _writer = new MediaFoundationMp4Writer(
                _outputPath,
                _captureSession.NativeDevicePointer,
                _captureSession.Width,
                _captureSession.Height,
                30,
                includeAudioStream);

            _writer.Start();
            if (includeAudioStream)
            {
                _audioMixer?.Start(_writer, HandleRuntimeFailure);
            }

            var startResult = _captureSession.Start();
            if (!startResult.IsSuccess)
            {
                await CleanupAsync(false, CancellationToken.None);
                return OperationResult<RecordingSessionInfo>.Failure(startResult.Error!);
            }

            _isRecording = true;
            return OperationResult<RecordingSessionInfo>.Success(
                new RecordingSessionInfo(
                    _outputPath,
                    _captureSession.Width,
                    _captureSession.Height,
                    30,
                    includeSystemAudio,
                    includeMicrophone,
                    warnings.Count == 0 ? null : string.Join(" ", warnings)));
        }
        catch (Exception ex)
        {
            await CleanupAsync(false, CancellationToken.None);
            return OperationResult<RecordingSessionInfo>.Failure(
                AppError.RecordingFailed($"ScreenFast could not initialize recording: {ex.Message}"));
        }
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_isRecording || _writer is null)
        {
            return Task.FromResult(OperationResult.Failure(AppError.InvalidState("There is no active recording to pause.")));
        }

        if (_isPaused)
        {
            return Task.FromResult(OperationResult.Failure(AppError.InvalidState("Recording is already paused.")));
        }

        var captureResult = _captureSession?.Pause() ?? OperationResult.Success();
        if (!captureResult.IsSuccess)
        {
            return Task.FromResult(captureResult);
        }

        var systemAudioResult = _systemAudioSession?.Pause() ?? OperationResult.Success();
        if (!systemAudioResult.IsSuccess)
        {
            return Task.FromResult(systemAudioResult);
        }

        var microphoneResult = _microphoneSession?.Pause() ?? OperationResult.Success();
        if (!microphoneResult.IsSuccess)
        {
            return Task.FromResult(microphoneResult);
        }

        lock (_sync)
        {
            _pauseStartedTimestamp = Stopwatch.GetTimestamp();
            _isPaused = true;
            _audioMixer?.Pause();
        }

        return Task.FromResult(OperationResult.Success());
    }

    public Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_isRecording || _writer is null)
        {
            return Task.FromResult(OperationResult.Failure(AppError.InvalidState("There is no paused recording to resume.")));
        }

        if (!_isPaused)
        {
            return Task.FromResult(OperationResult.Failure(AppError.InvalidState("Recording is not paused.")));
        }

        var captureResult = _captureSession?.Resume() ?? OperationResult.Success();
        if (!captureResult.IsSuccess)
        {
            return Task.FromResult(captureResult);
        }

        var systemAudioResult = _systemAudioSession?.Resume() ?? OperationResult.Success();
        if (!systemAudioResult.IsSuccess)
        {
            return Task.FromResult(systemAudioResult);
        }

        var microphoneResult = _microphoneSession?.Resume() ?? OperationResult.Success();
        if (!microphoneResult.IsSuccess)
        {
            return Task.FromResult(microphoneResult);
        }

        lock (_sync)
        {
            if (_pauseStartedTimestamp.HasValue)
            {
                var pausedTicks = Stopwatch.GetTimestamp() - _pauseStartedTimestamp.Value;
                _totalPausedDurationHundredsOfNanoseconds += ConvertStopwatchTicksToHundredsOfNanoseconds(pausedTicks);
            }

            _pauseStartedTimestamp = null;
            _isPaused = false;
            _audioMixer?.Resume();
        }

        return Task.FromResult(OperationResult.Success());
    }

    public async Task<OperationResult<string>> StopAsync(CancellationToken cancellationToken = default)
    {
        if ((!_isRecording && _backgroundFailure is null) || _writer is null || string.IsNullOrWhiteSpace(_outputPath))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("There is no active recording to stop."));
        }

        try
        {
            await CleanupAsync(true, cancellationToken);
            return _backgroundFailure is not null
                ? OperationResult<string>.Failure(_backgroundFailure)
                : OperationResult<string>.Success(_outputPath);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Failure(AppError.RecordingFailed($"ScreenFast could not finalize the MP4 file: {ex.Message}"));
        }
        finally
        {
            _isRecording = false;
            _isPaused = false;
            _outputPath = null;
            _pauseStartedTimestamp = null;
            _totalPausedDurationHundredsOfNanoseconds = 0;
        }
    }

    public void Dispose()
    {
        CleanupAsync(false, CancellationToken.None).GetAwaiter().GetResult();
        _lifetime.Dispose();
    }

    private OperationResult ProcessVideoFrame(CapturedFrame frame)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return OperationResult.Failure(AppError.RecordingFailed("The video writer is not available."));
            }

            if (_isPaused)
            {
                return OperationResult.Success();
            }

            try
            {
                _writer.WriteVideoFrame(frame.TexturePointer, AdjustVideoTimestamp(frame.TimestampHundredsOfNanoseconds));
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                var error = AppError.RecordingFailed($"A video frame could not be written: {ex.Message}");
                HandleRuntimeFailure(error);
                return OperationResult.Failure(error);
            }
        }
    }

    private void OnAudioChunk(AudioChunk chunk)
    {
        try
        {
            if (_isPaused)
            {
                return;
            }

            _audioMixer?.AddChunk(chunk);
        }
        catch (Exception ex)
        {
            HandleRuntimeFailure(AppError.AudioCaptureFailed($"ScreenFast could not process audio: {ex.Message}"));
        }
    }

    private long AdjustVideoTimestamp(long sourceTimestampHundredsOfNanoseconds)
    {
        var pausedDuration = _totalPausedDurationHundredsOfNanoseconds;
        if (_pauseStartedTimestamp.HasValue)
        {
            pausedDuration += ConvertStopwatchTicksToHundredsOfNanoseconds(Stopwatch.GetTimestamp() - _pauseStartedTimestamp.Value);
        }

        return Math.Max(0, sourceTimestampHundredsOfNanoseconds - pausedDuration);
    }

    private void HandleRuntimeFailure(AppError error)
    {
        lock (_sync)
        {
            _backgroundFailure ??= error;
        }

        if (Interlocked.Exchange(ref _runtimeFaulted, 1) == 1)
        {
            return;
        }

        RuntimeErrorOccurred?.Invoke(this, error);
        _ = Task.Run(() => CleanupAsync(true, CancellationToken.None));
    }

    private async Task CleanupAsync(bool finalizeWriter, CancellationToken cancellationToken)
    {
        var writer = Interlocked.Exchange(ref _writer, null);
        var captureSession = Interlocked.Exchange(ref _captureSession, null);
        var systemAudioSession = Interlocked.Exchange(ref _systemAudioSession, null);
        var microphoneSession = Interlocked.Exchange(ref _microphoneSession, null);
        var audioMixer = Interlocked.Exchange(ref _audioMixer, null);

        if (systemAudioSession is not null)
        {
            await systemAudioSession.StopAsync(cancellationToken);
            await systemAudioSession.DisposeAsync();
        }

        if (microphoneSession is not null)
        {
            await microphoneSession.StopAsync(cancellationToken);
            await microphoneSession.DisposeAsync();
        }

        if (audioMixer is not null)
        {
            await audioMixer.StopAsync();
        }

        if (captureSession is not null)
        {
            await captureSession.StopAsync(cancellationToken);
            await captureSession.DisposeAsync();
        }

        if (writer is not null)
        {
            if (finalizeWriter)
            {
                try
                {
                    writer.FinalizeFile();
                }
                catch (Exception ex)
                {
                    _backgroundFailure ??= AppError.RecordingFailed($"ScreenFast could not finalize the MP4 file: {ex.Message}");
                }
            }

            writer.Dispose();
        }

        _isRecording = false;
        _isPaused = false;
        _pauseStartedTimestamp = null;
        _totalPausedDurationHundredsOfNanoseconds = 0;
    }

    private static string BuildOutputPath(string outputFolder)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(outputFolder, $"ScreenFast-{timestamp}.mp4");
    }

    private static long ConvertStopwatchTicksToHundredsOfNanoseconds(long stopwatchTicks)
    {
        return (long)(stopwatchTicks * 10_000_000d / Stopwatch.Frequency);
    }

    private sealed class MediaFoundationLifetime : IDisposable
    {
        private static readonly object Gate = new();
        private static int _refCount;

        public MediaFoundationLifetime()
        {
            lock (Gate)
            {
                if (_refCount == 0)
                {
                    MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFStartup(MediaFoundationNative.MFVersion, MediaFoundationNative.MFStartupFull));
                }

                _refCount++;
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (_refCount == 0)
                {
                    return;
                }

                _refCount--;
                if (_refCount == 0)
                {
                    MediaFoundationNative.MFShutdown();
                }
            }
        }
    }

    private sealed class AudioMixerPump
    {
        private readonly BufferedWaveProvider _systemAudioBuffer;
        private readonly BufferedWaveProvider _microphoneBuffer;
        private readonly MixingSampleProvider _mixingProvider;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _pumpTask;
        private MediaFoundationMp4Writer? _writer;
        private Action<AppError>? _runtimeErrorHandler;
        private bool _isPaused;

        public AudioMixerPump()
        {
            _systemAudioBuffer = CreateBuffer();
            _microphoneBuffer = CreateBuffer();
            _mixingProvider = new MixingSampleProvider(new[]
            {
                _systemAudioBuffer.ToSampleProvider(),
                _microphoneBuffer.ToSampleProvider()
            })
            {
                ReadFully = true
            };
        }

        public void Start(MediaFoundationMp4Writer writer, Action<AppError> runtimeErrorHandler)
        {
            _writer = writer;
            _runtimeErrorHandler = runtimeErrorHandler;
            _pumpTask = Task.Run(() => PumpAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        public void Pause()
        {
            _isPaused = true;
            ClearBuffers();
        }

        public void Resume()
        {
            ClearBuffers();
            _isPaused = false;
        }

        public void AddChunk(AudioChunk chunk)
        {
            if (chunk.BytesRecorded <= 0 || _isPaused)
            {
                return;
            }

            var target = chunk.Kind == AudioInputKind.SystemAudio ? _systemAudioBuffer : _microphoneBuffer;
            target.AddSamples(chunk.Buffer, 0, chunk.BytesRecorded);
        }

        public async Task StopAsync()
        {
            _cancellationTokenSource.Cancel();
            if (_pumpTask is not null)
            {
                try
                {
                    await _pumpTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _cancellationTokenSource.Dispose();
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            try
            {
                var framesPerChunk = 48_000 / 50;
                var samplesPerChunk = framesPerChunk * 2;
                var mixedSamples = new float[samplesPerChunk];

                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    if (_isPaused || _writer is null)
                    {
                        continue;
                    }

                    var samplesRead = _mixingProvider.Read(mixedSamples, 0, mixedSamples.Length);
                    if (samplesRead <= 0)
                    {
                        continue;
                    }

                    var sampleFrames = samplesRead / 2;
                    var pcmBytes = new byte[sampleFrames * 2 * sizeof(short)];
                    ConvertFloatToPcm16(mixedSamples, samplesRead, pcmBytes);
                    _writer.WriteAudioSamples(pcmBytes, sampleFrames);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _runtimeErrorHandler?.Invoke(AppError.AudioCaptureFailed($"ScreenFast could not mix audio while recording: {ex.Message}"));
            }
        }

        private void ClearBuffers()
        {
            _systemAudioBuffer.ClearBuffer();
            _microphoneBuffer.ClearBuffer();
        }

        private static BufferedWaveProvider CreateBuffer()
        {
            return new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2))
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
        }

        private static void ConvertFloatToPcm16(float[] source, int samplesRead, byte[] destination)
        {
            for (var i = 0; i < samplesRead; i++)
            {
                var clamped = Math.Clamp(source[i], -1f, 1f);
                var sample = (short)Math.Round(clamped * short.MaxValue);
                destination[i * 2] = (byte)(sample & 0xFF);
                destination[(i * 2) + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }
    }

    private sealed class MediaFoundationMp4Writer : IDisposable
    {
        private readonly MediaFoundationNative.IMFSinkWriter _sinkWriter;
        private readonly MediaFoundationNative.IMFDXGIDeviceManager _deviceManager;
        private readonly uint _videoStreamIndex;
        private readonly uint? _audioStreamIndex;
        private readonly long _defaultFrameDuration;
        private readonly object _writerGate = new();

        private long? _firstVideoTimestamp;
        private long? _lastVideoTimestamp;
        private long _audioFramesWritten;
        private bool _isFinalized;

        public MediaFoundationMp4Writer(string outputPath, nint devicePointer, int width, int height, int frameRate, bool includeAudio)
        {
            _defaultFrameDuration = 10_000_000L / frameRate;

            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateDXGIDeviceManager(out var resetToken, out _deviceManager));
            MediaFoundationNative.ThrowIfFailed(_deviceManager.ResetDevice(devicePointer, resetToken));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateAttributes(out var attributes, 4));

            MediaFoundationNative.IMFMediaType? outputVideoType = null;
            MediaFoundationNative.IMFMediaType? inputVideoType = null;
            MediaFoundationNative.IMFMediaType? outputAudioType = null;
            MediaFoundationNative.IMFMediaType? inputAudioType = null;

            try
            {
                MediaFoundationNative.ThrowIfFailed(attributes.SetUINT32(MediaFoundationNative.MFReadwriteEnableHardwareTransforms, 1));
                MediaFoundationNative.ThrowIfFailed(attributes.SetUnknown(MediaFoundationNative.MFSinkWriterD3DManager, _deviceManager));
                MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateSinkWriterFromURL(outputPath, nint.Zero, attributes, out _sinkWriter));

                outputVideoType = CreateOutputVideoMediaType(width, height, frameRate);
                MediaFoundationNative.ThrowIfFailed(_sinkWriter.AddStream(outputVideoType, out _videoStreamIndex));

                inputVideoType = CreateInputVideoMediaType(width, height, frameRate);
                MediaFoundationNative.ThrowIfFailed(_sinkWriter.SetInputMediaType(_videoStreamIndex, inputVideoType, null));

                if (includeAudio)
                {
                    outputAudioType = CreateOutputAudioMediaType();
                    MediaFoundationNative.ThrowIfFailed(_sinkWriter.AddStream(outputAudioType, out var audioStreamIndex));
                    _audioStreamIndex = audioStreamIndex;

                    inputAudioType = CreateInputAudioMediaType();
                    MediaFoundationNative.ThrowIfFailed(_sinkWriter.SetInputMediaType(audioStreamIndex, inputAudioType, null));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);
                ReleaseComObject(outputVideoType);
                ReleaseComObject(inputVideoType);
                ReleaseComObject(outputAudioType);
                ReleaseComObject(inputAudioType);
            }
        }

        public void Start()
        {
            MediaFoundationNative.ThrowIfFailed(_sinkWriter.BeginWriting());
        }

        public void WriteVideoFrame(nint texturePointer, long timestampHundredsOfNanoseconds)
        {
            lock (_writerGate)
            {
                ThrowIfFinalized();

                _firstVideoTimestamp ??= timestampHundredsOfNanoseconds;
                var relativeTimestamp = Math.Max(0, timestampHundredsOfNanoseconds - _firstVideoTimestamp.Value);
                var duration = _lastVideoTimestamp.HasValue
                    ? Math.Max(_defaultFrameDuration, relativeTimestamp - _lastVideoTimestamp.Value)
                    : _defaultFrameDuration;

                MediaFoundationNative.ThrowIfFailed(
                    MediaFoundationNative.MFCreateDXGISurfaceBuffer(
                        Direct3D11Texture2DGuid,
                        texturePointer,
                        0,
                        false,
                        out var mediaBuffer));

                try
                {
                    MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateSample(out var sample));
                    try
                    {
                        MediaFoundationNative.ThrowIfFailed(sample.AddBuffer(mediaBuffer));
                        MediaFoundationNative.ThrowIfFailed(sample.SetSampleTime(relativeTimestamp));
                        MediaFoundationNative.ThrowIfFailed(sample.SetSampleDuration(duration));
                        MediaFoundationNative.ThrowIfFailed(_sinkWriter.WriteSample(_videoStreamIndex, sample));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaBuffer);
                }

                _lastVideoTimestamp = relativeTimestamp;
            }
        }

        public void WriteAudioSamples(byte[] pcm16Payload, int sampleFrames)
        {
            if (_audioStreamIndex is null || sampleFrames <= 0)
            {
                return;
            }

            lock (_writerGate)
            {
                ThrowIfFinalized();

                var timestamp = _audioFramesWritten * 10_000_000L / 48_000L;
                var duration = sampleFrames * 10_000_000L / 48_000L;

                MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMemoryBuffer((uint)pcm16Payload.Length, out var mediaBuffer));
                try
                {
                    MediaFoundationNative.ThrowIfFailed(mediaBuffer.Lock(out var bufferPointer, out _, out _));
                    try
                    {
                        Marshal.Copy(pcm16Payload, 0, bufferPointer, pcm16Payload.Length);
                    }
                    finally
                    {
                        mediaBuffer.Unlock();
                    }

                    MediaFoundationNative.ThrowIfFailed(mediaBuffer.SetCurrentLength((uint)pcm16Payload.Length));
                    MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateSample(out var sample));
                    try
                    {
                        MediaFoundationNative.ThrowIfFailed(sample.AddBuffer(mediaBuffer));
                        MediaFoundationNative.ThrowIfFailed(sample.SetSampleTime(timestamp));
                        MediaFoundationNative.ThrowIfFailed(sample.SetSampleDuration(duration));
                        MediaFoundationNative.ThrowIfFailed(_sinkWriter.WriteSample(_audioStreamIndex.Value, sample));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(mediaBuffer);
                }

                _audioFramesWritten += sampleFrames;
            }
        }

        public void FinalizeFile()
        {
            lock (_writerGate)
            {
                if (_isFinalized)
                {
                    return;
                }

                MediaFoundationNative.ThrowIfFailed(_sinkWriter.Finalize_());
                _isFinalized = true;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_isFinalized)
                {
                    FinalizeFile();
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(_sinkWriter);
                Marshal.ReleaseComObject(_deviceManager);
            }
        }

        private void ThrowIfFinalized()
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("The MP4 writer has already been finalized.");
            }
        }

        private static void ReleaseComObject(object? comObject)
        {
            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }

        private static readonly Guid Direct3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

        private static MediaFoundationNative.IMFMediaType CreateOutputVideoMediaType(int width, int height, int frameRate)
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMediaType(out var mediaType));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtMajorType, MediaFoundationNative.MFMediaTypeVideo));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtSubtype, MediaFoundationNative.MFVideoFormatH264));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAvgBitrate, CalculateBitrate(width, height, frameRate)));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtInterlaceMode, MediaFoundationNative.ProgressiveInterlaceMode));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeSize(mediaType, MediaFoundationNative.MFMtFrameSize, (uint)width, (uint)height));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeRatio(mediaType, MediaFoundationNative.MFMtFrameRate, (uint)frameRate, 1));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeRatio(mediaType, MediaFoundationNative.MFMtPixelAspectRatio, 1, 1));
            return mediaType;
        }

        private static MediaFoundationNative.IMFMediaType CreateInputVideoMediaType(int width, int height, int frameRate)
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMediaType(out var mediaType));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtMajorType, MediaFoundationNative.MFMediaTypeVideo));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtSubtype, MediaFoundationNative.MFVideoFormatArgb32));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtInterlaceMode, MediaFoundationNative.ProgressiveInterlaceMode));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeSize(mediaType, MediaFoundationNative.MFMtFrameSize, (uint)width, (uint)height));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeRatio(mediaType, MediaFoundationNative.MFMtFrameRate, (uint)frameRate, 1));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFSetAttributeRatio(mediaType, MediaFoundationNative.MFMtPixelAspectRatio, 1, 1));
            return mediaType;
        }

        private static MediaFoundationNative.IMFMediaType CreateOutputAudioMediaType()
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMediaType(out var mediaType));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtMajorType, MediaFoundationNative.MFMediaTypeAudio));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtSubtype, MediaFoundationNative.MFAudioFormatAac));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioNumChannels, 2));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioSamplesPerSecond, 48_000));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioAvgBytesPerSecond, 24_000));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioBitsPerSample, 16));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAacPayloadType, 0));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAacAudioProfileLevelIndication, 0x29));
            return mediaType;
        }

        private static MediaFoundationNative.IMFMediaType CreateInputAudioMediaType()
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMediaType(out var mediaType));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtMajorType, MediaFoundationNative.MFMediaTypeAudio));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtSubtype, MediaFoundationNative.MFAudioFormatPcm));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioNumChannels, 2));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioSamplesPerSecond, 48_000));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioBitsPerSample, 16));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioValidBitsPerSample, 16));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioBlockAlignment, 4));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioAvgBytesPerSecond, 192_000));
            return mediaType;
        }

        private static uint CalculateBitrate(int width, int height, int frameRate)
        {
            var pixelsPerSecond = width * height * frameRate;
            return (uint)Math.Clamp(pixelsPerSecond / 4, 4_000_000, 20_000_000);
        }
    }
}
