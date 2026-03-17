using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScreenFast.Audio.Internal;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Audio.Services;

internal abstract class WasapiAudioCaptureServiceBase
{
    private readonly IScreenFastLogService _logService;

    protected WasapiAudioCaptureServiceBase(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    protected async Task<OperationResult<IAudioCaptureSession>> StartAsync(
        AudioInputKind kind,
        Func<MMDevice, WasapiCapture> captureFactory,
        DataFlow dataFlow,
        Action<AudioChunk> onAudioChunk,
        Action<AppError> onError,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
            var capture = captureFactory(device);
            var session = new WasapiAudioCaptureSession(kind, device, capture, onAudioChunk, onError, _logService);

            var startResult = await session.StartAsync(cancellationToken);
            if (!startResult.IsSuccess)
            {
                await session.DisposeAsync();
                return OperationResult<IAudioCaptureSession>.Failure(startResult.Error!);
            }

            _logService.Info("audio.capture_started", "ScreenFast audio capture started.", new Dictionary<string, object?> { ["kind"] = kind.ToString() });
            return OperationResult<IAudioCaptureSession>.Success(session);
        }
        catch (Exception ex)
        {
            _logService.Warning("audio.capture_start_failed", ex.Message, new Dictionary<string, object?> { ["kind"] = kind.ToString() });
            return OperationResult<IAudioCaptureSession>.Failure(AudioErrorFactory.DeviceUnavailable(kind, ex.Message));
        }
    }

    private sealed class WasapiAudioCaptureSession : IAudioCaptureSession
    {
        private readonly AudioInputKind _kind;
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly Action<AudioChunk> _onAudioChunk;
        private readonly Action<AppError> _onError;
        private readonly IScreenFastLogService _logService;
        private readonly BufferedWaveProvider _bufferedProvider;
        private readonly ISampleProvider _sampleProvider;
        private readonly CancellationTokenSource _pumpCancellation = new();
        private readonly object _gate = new();

        private Task? _pumpTask;
        private long _sampleFramesEmitted;
        private bool _isStopped;
        private bool _hasFaulted;
        private bool _isPaused;

        public WasapiAudioCaptureSession(
            AudioInputKind kind,
            MMDevice device,
            WasapiCapture capture,
            Action<AudioChunk> onAudioChunk,
            Action<AppError> onError,
            IScreenFastLogService logService)
        {
            _kind = kind;
            _device = device;
            _capture = capture;
            _onAudioChunk = onAudioChunk;
            _onError = onError;
            _logService = logService;
            _bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(5)
            };
            _sampleProvider = SampleProviderFactory.CreateStandardProvider(_bufferedProvider);
        }

        public Task<OperationResult> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _pumpTask = Task.Run(() => PumpAsync(_pumpCancellation.Token), _pumpCancellation.Token);
                return Task.FromResult(OperationResult.Success());
            }
            catch (Exception ex)
            {
                _logService.Warning("audio.capture_start_failed", ex.Message, new Dictionary<string, object?> { ["kind"] = _kind.ToString() });
                return Task.FromResult(OperationResult.Failure(AudioErrorFactory.DeviceUnavailable(_kind, ex.Message)));
            }
        }

        public OperationResult Pause()
        {
            if (_isStopped)
            {
                return OperationResult.Failure(AppError.InvalidState("Audio capture is not active."));
            }

            _isPaused = true;
            _bufferedProvider.ClearBuffer();
            _logService.Info("audio.capture_paused", "ScreenFast audio capture paused.", new Dictionary<string, object?> { ["kind"] = _kind.ToString() });
            return OperationResult.Success();
        }

        public OperationResult Resume()
        {
            if (_isStopped)
            {
                return OperationResult.Failure(AppError.InvalidState("Audio capture is not active."));
            }

            _bufferedProvider.ClearBuffer();
            _isPaused = false;
            _logService.Info("audio.capture_resumed", "ScreenFast audio capture resumed.", new Dictionary<string, object?> { ["kind"] = _kind.ToString() });
            return OperationResult.Success();
        }

        public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (_isStopped)
            {
                return OperationResult.Success();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _isStopped = true;
            _pumpCancellation.Cancel();

            try
            {
                _capture.StopRecording();
            }
            catch
            {
            }

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

            _logService.Info("audio.capture_stopped", "ScreenFast audio capture stopped.", new Dictionary<string, object?> { ["kind"] = _kind.ToString() });
            return OperationResult.Success();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _device.Dispose();
            _pumpCancellation.Dispose();
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            var framesPerChunk = AudioFormatConstants.TargetSampleRate * AudioFormatConstants.PumpMilliseconds / 1000;
            var samplesPerChunk = framesPerChunk * AudioFormatConstants.TargetChannels;
            var floatSamples = new float[samplesPerChunk];

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AudioFormatConstants.PumpMilliseconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var samplesRead = _sampleProvider.Read(floatSamples, 0, floatSamples.Length);
                if (samplesRead <= 0)
                {
                    continue;
                }

                if (_isPaused)
                {
                    continue;
                }

                var sampleFrames = samplesRead / AudioFormatConstants.TargetChannels;
                var payload = new byte[samplesRead * sizeof(float)];
                Buffer.BlockCopy(floatSamples, 0, payload, 0, payload.Length);

                var timestamp = _sampleFramesEmitted * 10_000_000L / AudioFormatConstants.TargetSampleRate;
                _sampleFramesEmitted += sampleFrames;

                _onAudioChunk(
                    new AudioChunk(
                        _kind,
                        payload,
                        payload.Length,
                        AudioFormatConstants.TargetSampleRate,
                        AudioFormatConstants.TargetChannels,
                        32,
                        true,
                        sampleFrames,
                        timestamp));
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            if (_isStopped || args.BytesRecorded <= 0)
            {
                return;
            }

            try
            {
                _bufferedProvider.AddSamples(args.Buffer, 0, args.BytesRecorded);
            }
            catch (Exception ex)
            {
                NotifyFault(ex.Message);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (_isStopped)
            {
                return;
            }

            if (args.Exception is not null)
            {
                NotifyFault(args.Exception.Message);
            }
        }

        private void NotifyFault(string message)
        {
            lock (_gate)
            {
                if (_hasFaulted)
                {
                    return;
                }

                _hasFaulted = true;
            }

            _logService.Warning("audio.capture_runtime_failure", message, new Dictionary<string, object?> { ["kind"] = _kind.ToString() });
            _onError(AudioErrorFactory.RuntimeFailure(_kind, message));
        }
    }
}
