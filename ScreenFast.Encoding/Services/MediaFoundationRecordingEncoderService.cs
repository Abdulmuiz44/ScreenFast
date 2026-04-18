using System.Collections.Concurrent;
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
    private readonly IScreenFastLogService _logService;
    private readonly object _sync = new();
    private readonly MediaFoundationLifetime _lifetime = new();

    private ICaptureSession? _captureSession;
    private IAudioCaptureSession? _systemAudioSession;
    private IAudioCaptureSession? _microphoneSession;
    private MediaFoundationMp4Writer? _writer;
    private AudioMixerPump? _audioMixer;
    private string? _outputPath;
    private PendingWriterInitialization? _pendingWriterInitialization;
    private AppError? _backgroundFailure;
    private int _runtimeFaulted;
    private bool _isRecording;
    private bool _isPaused;
    private long? _pauseStartedTimestamp;
    private long _totalPausedDurationHundredsOfNanoseconds;

    public MediaFoundationRecordingEncoderService(
        ICaptureSessionFactory captureSessionFactory,
        ISystemAudioCaptureService systemAudioCaptureService,
        IMicrophoneCaptureService microphoneCaptureService,
        IScreenFastLogService logService)
    {
        _captureSessionFactory = captureSessionFactory;
        _systemAudioCaptureService = systemAudioCaptureService;
        _microphoneCaptureService = microphoneCaptureService;
        _logService = logService;
    }

    public event EventHandler<AppError>? RuntimeErrorOccurred;

    public async Task<OperationResult<RecordingSessionInfo>> StartAsync(RecordingStartRequest request, CancellationToken cancellationToken = default)
    {
        _logService.Info(
            "encoder.start_requested",
            "ScreenFast encoder start was requested.",
            new Dictionary<string, object?>
            {
                ["outputFolder"] = request.OutputFolder,
                ["outputFilePath"] = request.OutputFilePath,
                ["qualityPreset"] = request.QualityPreset,
                ["includeSystemAudio"] = request.IncludeSystemAudio,
                ["includeMicrophone"] = request.IncludeMicrophone,
                ["sourceSummary"] = request.Source.DisplayName
            });

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
            _logService.Warning(
                "encoder.capture_session_failed",
                captureSessionResult.Error?.Message ?? "Capture session creation failed.");
            return OperationResult<RecordingSessionInfo>.Failure(captureSessionResult.Error!);
        }

        _captureSession = captureSessionResult.Value;
        _outputPath = request.OutputFilePath;

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
                    _logService.Warning("encoder.system_audio_unavailable", warnings[^1]);
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
                    _logService.Warning("encoder.microphone_unavailable", warnings[^1]);
                }
            }

            var qualityDefinition = VideoQualityPresets.Get(request.QualityPreset);
            var includeAudioStream = includeSystemAudio || includeMicrophone;
            _pendingWriterInitialization = new PendingWriterInitialization(
                qualityDefinition,
                includeAudioStream,
                includeSystemAudio,
                includeMicrophone,
                request.QualityPreset);

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
                    qualityDefinition.FrameRate,
                    includeSystemAudio,
                    includeMicrophone,
                    request.QualityPreset,
                    warnings.Count == 0 ? null : string.Join(" ", warnings)));
        }
        catch (Exception ex)
        {
            _logService.Error(
                "encoder.start_failed",
                "ScreenFast could not initialize recording.",
                new Dictionary<string, object?>
                {
                    ["outputPath"] = _outputPath,
                    ["error"] = ex.Message
                });
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

        _logService.Info("encoder.paused", "ScreenFast encoder paused.");
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

        _logService.Info("encoder.resumed", "ScreenFast encoder resumed.");
        return Task.FromResult(OperationResult.Success());
    }

    public async Task<OperationResult<string>> StopAsync(CancellationToken cancellationToken = default)
    {
        _logService.Info("encoder.stop_requested", "ScreenFast encoder stop was requested.");

        if ((!_isRecording && _backgroundFailure is null) || string.IsNullOrWhiteSpace(_outputPath))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("There is no active recording to stop."));
        }

        var outputPath = _outputPath;

        try
        {
            await CleanupAsync(true, cancellationToken);
            if (_backgroundFailure is not null)
            {
                _logService.Warning(
                    "encoder.stop_completed_with_failure",
                    _backgroundFailure.Message,
                    new Dictionary<string, object?> { ["outputPath"] = outputPath });
                return OperationResult<string>.Failure(_backgroundFailure);
            }

            _logService.Info(
                "encoder.stopped",
                "ScreenFast encoder stopped cleanly.",
                new Dictionary<string, object?> { ["outputPath"] = outputPath });
            return OperationResult<string>.Success(outputPath);
        }
        catch (Exception ex)
        {
            _logService.Error(
                "encoder.stop_failed",
                "ScreenFast could not finalize the MP4 file.",
                new Dictionary<string, object?>
                {
                    ["outputPath"] = outputPath,
                    ["error"] = ex.Message
                });
            return OperationResult<string>.Failure(AppError.RecordingFailed($"ScreenFast could not finalize the MP4 file: {ex.Message}"));
        }
        finally
        {
            _isRecording = false;
            _isPaused = false;
            _outputPath = null;
            _pendingWriterInitialization = null;
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
            if (_isPaused)
            {
                return OperationResult.Success();
            }

            try
            {
                EnsureWriterInitialized(frame.Width, frame.Height);
                _writer.WriteVideoFrame(frame.TexturePointer, AdjustVideoTimestamp(frame.TimestampHundredsOfNanoseconds));
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                var error = AppError.RecordingFailed($"A video frame could not be written: {BuildVideoFrameErrorMessage(ex)}");
                HandleRuntimeFailure(error);
                return OperationResult.Failure(error);
            }
        }
    }

    private void EnsureWriterInitialized(int frameWidth, int frameHeight)
    {
        if (_writer is not null)
        {
            return;
        }

        if (_captureSession is null || string.IsNullOrWhiteSpace(_outputPath) || _pendingWriterInitialization is null)
        {
            throw new InvalidOperationException("The video writer could not be initialized for the first frame.");
        }

        _writer = new MediaFoundationMp4Writer(
            _outputPath,
            _captureSession.NativeDevicePointer,
            frameWidth,
            frameHeight,
            _pendingWriterInitialization.QualityDefinition,
            _pendingWriterInitialization.IncludeAudioStream);

        _writer.Start();
        if (_pendingWriterInitialization.IncludeAudioStream)
        {
            _audioMixer?.Start(_writer, HandleRuntimeFailure);
        }

        _logService.Info(
            "encoder.started",
            "ScreenFast encoder started successfully.",
            new Dictionary<string, object?>
            {
                ["outputPath"] = _outputPath,
                ["width"] = frameWidth,
                ["height"] = frameHeight,
                ["includeSystemAudio"] = _pendingWriterInitialization.IncludeSystemAudio,
                ["includeMicrophone"] = _pendingWriterInitialization.IncludeMicrophone,
                ["qualityPreset"] = _pendingWriterInitialization.QualityPreset
            });

        _pendingWriterInitialization = null;
    }

    private static string BuildVideoFrameErrorMessage(Exception exception)
    {
        var messages = new List<string>();
        var current = exception;

        while (current is not null)
        {
            messages.Add(current.Message);
            current = current.InnerException;
        }

        return string.Join(" => ", messages.Distinct());
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

        _logService.Warning(
            "encoder.runtime_failure",
            error.Message,
            new Dictionary<string, object?>
            {
                ["outputPath"] = _outputPath,
                ["isPaused"] = _isPaused
            });

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
            if (finalizeWriter && _backgroundFailure is null && writer.HasWrittenVideoFrames)
            {
                try
                {
                    writer.FinalizeFile();
                }
                catch (Exception ex)
                {
                    _backgroundFailure ??= AppError.RecordingFailed($"ScreenFast could not finalize the MP4 file: {ex.Message}");
                    _logService.Warning("encoder.finalize_failed", ex.Message);
                }
            }
            else if (finalizeWriter && _backgroundFailure is null)
            {
                _backgroundFailure = AppError.RecordingFailed("ScreenFast stopped before any video frames were captured, so there was no MP4 file to finalize.");
                _logService.Warning("encoder.finalize_skipped", _backgroundFailure.Message, new Dictionary<string, object?> { ["outputPath"] = _outputPath });
            }

            writer.Dispose();
        }

        _isRecording = false;
        _isPaused = false;
        _pauseStartedTimestamp = null;
        _totalPausedDurationHundredsOfNanoseconds = 0;
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
                _runtimeErrorHandler?.Invoke(AppError.AudioCaptureFailed($"ScreenFast could not mix audio while recording: {BuildAudioMixErrorMessage(ex)}"));
            }
        }

        private static string BuildAudioMixErrorMessage(Exception exception)
        {
            var current = exception;
            while (current.InnerException is not null)
            {
                current = current.InnerException;
            }

            return current.Message;
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

    private sealed record PendingWriterInitialization(
        VideoQualityPresetDefinition QualityDefinition,
        bool IncludeAudioStream,
        bool IncludeSystemAudio,
        bool IncludeMicrophone,
        VideoQualityPreset QualityPreset);

    private sealed class MediaFoundationMp4Writer : IDisposable
    {
        private readonly BlockingCollection<WriterWorkItem> _writerQueue = new();
        private readonly Thread _writerThread;
        private readonly long _defaultFrameDuration;

        private MediaFoundationNative.IMFSinkWriter? _sinkWriter;
        private MediaFoundationNative.IMFDXGIDeviceManager? _deviceManager;
        private uint _videoStreamIndex;
        private uint? _audioStreamIndex;
        private long? _firstVideoTimestamp;
        private long? _lastVideoTimestamp;
        private long _audioFramesWritten;
        private long _videoFramesWritten;
        private bool _isFinalized;
        private bool _isDisposed;

        public bool HasWrittenVideoFrames => _videoFramesWritten > 0;

        public MediaFoundationMp4Writer(string outputPath, nint devicePointer, int width, int height, VideoQualityPresetDefinition qualityDefinition, bool includeAudio)
        {
            _defaultFrameDuration = 10_000_000L / qualityDefinition.FrameRate;

            var initialized = new ManualResetEventSlim();
            Exception? initializationError = null;

            _writerThread = new Thread(() =>
            {
                try
                {
                    InitializeWriter(outputPath, devicePointer, width, height, qualityDefinition, includeAudio);
                }
                catch (Exception ex)
                {
                    initializationError = ex;
                }
                finally
                {
                    initialized.Set();
                }

                if (initializationError is not null)
                {
                    ReleaseNativeObjects();
                    return;
                }

                try
                {
                    foreach (var workItem in _writerQueue.GetConsumingEnumerable())
                    {
                        try
                        {
                            workItem.Action();
                            workItem.Completion.SetResult();
                        }
                        catch (Exception ex)
                        {
                            workItem.Completion.SetException(ex);
                        }
                    }
                }
                finally
                {
                    ReleaseNativeObjects();
                }
            })
            {
                IsBackground = true,
                Name = "ScreenFast Media Foundation Writer"
            };
            _writerThread.SetApartmentState(ApartmentState.MTA);
            _writerThread.Start();

            initialized.Wait();
            initialized.Dispose();

            if (initializationError is not null)
            {
                throw initializationError;
            }
        }

        private void InitializeWriter(string outputPath, nint devicePointer, int width, int height, VideoQualityPresetDefinition qualityDefinition, bool includeAudio)
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateDXGIDeviceManager(out var resetToken, out var deviceManager));
            _deviceManager = deviceManager;
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
                MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateSinkWriterFromURL(outputPath, nint.Zero, attributes, out var sinkWriter));
                _sinkWriter = sinkWriter;

                outputVideoType = CreateOutputVideoMediaType(width, height, qualityDefinition.FrameRate, qualityDefinition.TargetBitrate);
                MediaFoundationNative.ThrowIfFailed(_sinkWriter.AddStream(outputVideoType, out _videoStreamIndex));

                inputVideoType = CreateInputVideoMediaType(width, height, qualityDefinition.FrameRate);
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
            InvokeOnWriterThread(() => MediaFoundationNative.ThrowIfFailed(_sinkWriter!.BeginWriting()));
        }

        public void WriteVideoFrame(nint texturePointer, long timestampHundredsOfNanoseconds)
        {
            InvokeOnWriterThread(() =>
            {
                ThrowIfFinalized();

                _firstVideoTimestamp ??= timestampHundredsOfNanoseconds;
                var sourceRelativeTimestamp = Math.Max(0, timestampHundredsOfNanoseconds - _firstVideoTimestamp.Value);
                var relativeTimestamp = _lastVideoTimestamp.HasValue
                    ? Math.Max(_lastVideoTimestamp.Value + _defaultFrameDuration, sourceRelativeTimestamp)
                    : sourceRelativeTimestamp;
                var duration = _defaultFrameDuration;

                MediaFoundationNative.IMFMediaBuffer mediaBuffer;
                try
                {
                    MediaFoundationNative.ThrowIfFailedWithContext(
                        MediaFoundationNative.MFCreateDXGISurfaceBuffer(
                            Direct3D11Texture2DGuid,
                            texturePointer,
                            0,
                            false,
                            out mediaBuffer),
                        "MFCreateDXGISurfaceBuffer(video)");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("MFCreateDXGISurfaceBuffer(video)", ex);
                }

                try
                {
                    MediaFoundationNative.IMFSample sample;
                    try
                    {
                        MediaFoundationNative.ThrowIfFailedWithContext(MediaFoundationNative.MFCreateSample(out sample), "MFCreateSample(video)");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("MFCreateSample(video)", ex);
                    }

                    try
                    {
                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.AddBuffer(mediaBuffer), "IMFSample.AddBuffer(video)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.AddBuffer(video)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.SetSampleTime(relativeTimestamp), "IMFSample.SetSampleTime(video)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.SetSampleTime(video)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.SetSampleDuration(duration), "IMFSample.SetSampleDuration(video)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.SetSampleDuration(video)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(_sinkWriter.WriteSample(_videoStreamIndex, sample), "IMFSinkWriter.WriteSample(video)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSinkWriter.WriteSample(video)", ex);
                        }
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
                _videoFramesWritten++;
            });
        }

        public void WriteAudioSamples(byte[] pcm16Payload, int sampleFrames)
        {
            if (_audioStreamIndex is null || sampleFrames <= 0)
            {
                return;
            }

            InvokeOnWriterThread(() =>
            {
                ThrowIfFinalized();

                var timestamp = _audioFramesWritten * 10_000_000L / 48_000L;
                var duration = sampleFrames * 10_000_000L / 48_000L;

                MediaFoundationNative.IMFMediaBuffer mediaBuffer;
                try
                {
                    MediaFoundationNative.ThrowIfFailedWithContext(
                        MediaFoundationNative.MFCreateMemoryBuffer((uint)pcm16Payload.Length, out mediaBuffer),
                        "MFCreateMemoryBuffer(audio)");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("MFCreateMemoryBuffer(audio)", ex);
                }

                try
                {
                    nint bufferPointer;
                    try
                    {
                        MediaFoundationNative.ThrowIfFailedWithContext(mediaBuffer.Lock(out bufferPointer, out _, out _), "IMFMediaBuffer.Lock(audio)");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("IMFMediaBuffer.Lock(audio)", ex);
                    }

                    try
                    {
                        Marshal.Copy(pcm16Payload, 0, bufferPointer, pcm16Payload.Length);
                    }
                    finally
                    {
                        mediaBuffer.Unlock();
                    }

                    try
                    {
                        MediaFoundationNative.ThrowIfFailedWithContext(mediaBuffer.SetCurrentLength((uint)pcm16Payload.Length), "IMFMediaBuffer.SetCurrentLength(audio)");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("IMFMediaBuffer.SetCurrentLength(audio)", ex);
                    }

                    MediaFoundationNative.IMFSample sample;
                    try
                    {
                        MediaFoundationNative.ThrowIfFailedWithContext(MediaFoundationNative.MFCreateSample(out sample), "MFCreateSample(audio)");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("MFCreateSample(audio)", ex);
                    }

                    try
                    {
                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.AddBuffer(mediaBuffer), "IMFSample.AddBuffer(audio)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.AddBuffer(audio)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.SetSampleTime(timestamp), "IMFSample.SetSampleTime(audio)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.SetSampleTime(audio)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(sample.SetSampleDuration(duration), "IMFSample.SetSampleDuration(audio)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSample.SetSampleDuration(audio)", ex);
                        }

                        try
                        {
                            MediaFoundationNative.ThrowIfFailedWithContext(_sinkWriter.WriteSample(_audioStreamIndex.Value, sample), "IMFSinkWriter.WriteSample(audio)");
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException("IMFSinkWriter.WriteSample(audio)", ex);
                        }
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
            });
        }

        public void FinalizeFile()
        {
            InvokeOnWriterThread(() =>
            {
                if (_isFinalized)
                {
                    return;
                }

                MediaFoundationNative.ThrowIfFailed(_sinkWriter!.Finalize_());
                _isFinalized = true;
            });

            CompleteWriterThread();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

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
                CompleteWriterThread();
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

        private void InvokeOnWriterThread(Action action)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MediaFoundationMp4Writer));
            }

            if (Thread.CurrentThread.ManagedThreadId == _writerThread.ManagedThreadId)
            {
                action();
                return;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _writerQueue.Add(new WriterWorkItem(action, completion));
            completion.Task.GetAwaiter().GetResult();
        }

        private void CompleteWriterThread()
        {
            if (!_writerQueue.IsAddingCompleted)
            {
                _writerQueue.CompleteAdding();
            }

            if (_writerThread.IsAlive)
            {
                _writerThread.Join();
            }
        }

        private void ReleaseNativeObjects()
        {
            ReleaseComObject(_sinkWriter);
            _sinkWriter = null;
            ReleaseComObject(_deviceManager);
            _deviceManager = null;
        }

        private readonly record struct WriterWorkItem(Action Action, TaskCompletionSource Completion);

        private static readonly Guid Direct3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

        private static MediaFoundationNative.IMFMediaType CreateOutputVideoMediaType(int width, int height, int frameRate, uint targetBitrate)
        {
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateMediaType(out var mediaType));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtMajorType, MediaFoundationNative.MFMediaTypeVideo));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetGUID(MediaFoundationNative.MFMtSubtype, MediaFoundationNative.MFVideoFormatH264));
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAvgBitrate, targetBitrate));
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
            MediaFoundationNative.ThrowIfFailed(mediaType.SetUINT32(MediaFoundationNative.MFMtAudioBlockAlignment, 1));
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
    }
}
