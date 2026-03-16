using System.Globalization;
using System.Runtime.InteropServices;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Encoding.Interop;

namespace ScreenFast.Encoding.Services;

public sealed class MediaFoundationRecordingEncoderService : IRecordingEncoderService, IDisposable
{
    private readonly ICaptureSessionFactory _captureSessionFactory;
    private readonly object _sync = new();
    private readonly MediaFoundationLifetime _lifetime = new();

    private ICaptureSession? _captureSession;
    private MediaFoundationMp4Writer? _writer;
    private string? _outputPath;
    private AppError? _backgroundFailure;
    private bool _isRecording;

    public MediaFoundationRecordingEncoderService(ICaptureSessionFactory captureSessionFactory)
    {
        _captureSessionFactory = captureSessionFactory;
    }

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

        var captureSessionResult = await _captureSessionFactory.CreateAsync(request.Source, ProcessFrame, cancellationToken);
        if (!captureSessionResult.IsSuccess || captureSessionResult.Value is null)
        {
            return OperationResult<RecordingSessionInfo>.Failure(captureSessionResult.Error!);
        }

        _captureSession = captureSessionResult.Value;
        _backgroundFailure = null;
        _outputPath = BuildOutputPath(request.OutputFolder);

        try
        {
            _writer = new MediaFoundationMp4Writer(_outputPath, _captureSession.NativeDevicePointer, _captureSession.Width, _captureSession.Height, 30);
            _writer.Start();

            var startResult = _captureSession.Start();
            if (!startResult.IsSuccess)
            {
                await _captureSession.DisposeAsync();
                _captureSession = null;
                _writer.Dispose();
                _writer = null;
                return OperationResult<RecordingSessionInfo>.Failure(startResult.Error!);
            }

            _isRecording = true;
            return OperationResult<RecordingSessionInfo>.Success(
                new RecordingSessionInfo(_outputPath, _captureSession.Width, _captureSession.Height, 30));
        }
        catch (Exception ex)
        {
            await SafeDisposeCaptureAsync();
            _writer?.Dispose();
            _writer = null;
            _outputPath = null;
            return OperationResult<RecordingSessionInfo>.Failure(
                AppError.RecordingFailed($"ScreenFast could not initialize MP4 recording: {ex.Message}"));
        }
    }

    public async Task<OperationResult<string>> StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRecording || _captureSession is null || _writer is null || string.IsNullOrWhiteSpace(_outputPath))
        {
            return OperationResult<string>.Failure(AppError.InvalidState("There is no active recording to stop."));
        }

        try
        {
            var stopCaptureResult = await _captureSession.StopAsync(cancellationToken);
            if (!stopCaptureResult.IsSuccess)
            {
                return OperationResult<string>.Failure(stopCaptureResult.Error!);
            }

            _writer.FinalizeFile();

            if (_backgroundFailure is not null)
            {
                return OperationResult<string>.Failure(_backgroundFailure);
            }

            return OperationResult<string>.Success(_outputPath);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Failure(AppError.RecordingFailed($"ScreenFast could not finalize the MP4 file: {ex.Message}"));
        }
        finally
        {
            _isRecording = false;
            _writer.Dispose();
            _writer = null;
            await SafeDisposeCaptureAsync();
            _outputPath = null;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
        _captureSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _captureSession = null;
        _lifetime.Dispose();
    }

    private OperationResult ProcessFrame(CapturedFrame frame)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return OperationResult.Failure(AppError.RecordingFailed("The video writer is not available."));
            }

            try
            {
                _writer.WriteFrame(frame.TexturePointer, frame.TimestampHundredsOfNanoseconds);
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                _backgroundFailure ??= AppError.RecordingFailed($"A video frame could not be written: {ex.Message}");
                return OperationResult.Failure(_backgroundFailure);
            }
        }
    }

    private async Task SafeDisposeCaptureAsync()
    {
        if (_captureSession is not null)
        {
            await _captureSession.DisposeAsync();
            _captureSession = null;
        }
    }

    private static string BuildOutputPath(string outputFolder)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(outputFolder, $"ScreenFast-{timestamp}.mp4");
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

    private sealed class MediaFoundationMp4Writer : IDisposable
    {
        private readonly MediaFoundationNative.IMFSinkWriter _sinkWriter;
        private readonly MediaFoundationNative.IMFDXGIDeviceManager _deviceManager;
        private readonly uint _streamIndex;
        private readonly long _defaultFrameDuration;

        private long? _firstTimestamp;
        private long? _lastTimestamp;
        private bool _isFinalized;

        public MediaFoundationMp4Writer(string outputPath, nint devicePointer, int width, int height, int frameRate)
        {
            _defaultFrameDuration = 10_000_000L / frameRate;

            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateDXGIDeviceManager(out var resetToken, out _deviceManager));
            MediaFoundationNative.ThrowIfFailed(_deviceManager.ResetDevice(devicePointer, resetToken));
            MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateAttributes(out var attributes, 4));

            MediaFoundationNative.IMFMediaType? outputMediaType = null;
            MediaFoundationNative.IMFMediaType? inputMediaType = null;

            try
            {
                MediaFoundationNative.ThrowIfFailed(attributes.SetUINT32(MediaFoundationNative.MFReadwriteEnableHardwareTransforms, 1));
                MediaFoundationNative.ThrowIfFailed(attributes.SetUnknown(MediaFoundationNative.MFSinkWriterD3DManager, _deviceManager));
                MediaFoundationNative.ThrowIfFailed(MediaFoundationNative.MFCreateSinkWriterFromURL(outputPath, nint.Zero, attributes, out _sinkWriter));

                outputMediaType = CreateOutputMediaType(width, height, frameRate);
                MediaFoundationNative.ThrowIfFailed(_sinkWriter.AddStream(outputMediaType, out _streamIndex));

                inputMediaType = CreateInputMediaType(width, height, frameRate);
                MediaFoundationNative.ThrowIfFailed(_sinkWriter.SetInputMediaType(_streamIndex, inputMediaType, null));
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);

                if (outputMediaType is not null)
                {
                    Marshal.ReleaseComObject(outputMediaType);
                }

                if (inputMediaType is not null)
                {
                    Marshal.ReleaseComObject(inputMediaType);
                }
            }
        }

        public void Start()
        {
            MediaFoundationNative.ThrowIfFailed(_sinkWriter.BeginWriting());
        }

        public void WriteFrame(nint texturePointer, long timestampHundredsOfNanoseconds)
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("The MP4 writer has already been finalized.");
            }

            _firstTimestamp ??= timestampHundredsOfNanoseconds;
            var relativeTimestamp = Math.Max(0, timestampHundredsOfNanoseconds - _firstTimestamp.Value);
            var duration = _lastTimestamp.HasValue
                ? Math.Max(_defaultFrameDuration, relativeTimestamp - _lastTimestamp.Value)
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
                    MediaFoundationNative.ThrowIfFailed(_sinkWriter.WriteSample(_streamIndex, sample));
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

            _lastTimestamp = relativeTimestamp;
        }

        public void FinalizeFile()
        {
            if (_isFinalized)
            {
                return;
            }

            MediaFoundationNative.ThrowIfFailed(_sinkWriter.Finalize_());
            _isFinalized = true;
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
                // Dispose should not throw while tearing down a failed recording.
            }
            finally
            {
                Marshal.ReleaseComObject(_sinkWriter);
                Marshal.ReleaseComObject(_deviceManager);
            }
        }

        private static readonly Guid Direct3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

        private static MediaFoundationNative.IMFMediaType CreateOutputMediaType(int width, int height, int frameRate)
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

        private static MediaFoundationNative.IMFMediaType CreateInputMediaType(int width, int height, int frameRate)
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

        private static uint CalculateBitrate(int width, int height, int frameRate)
        {
            var pixelsPerSecond = width * height * frameRate;
            return (uint)Math.Clamp(pixelsPerSecond / 4, 4_000_000, 20_000_000);
        }
    }
}
