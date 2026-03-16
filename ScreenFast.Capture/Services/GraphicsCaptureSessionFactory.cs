using ScreenFast.Capture.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenFast.Capture.Services;

public sealed class GraphicsCaptureSessionFactory : ICaptureSessionFactory
{
    private readonly ICaptureItemResolver _captureItemResolver;
    private readonly Direct3D11DeviceProvider _deviceProvider;

    public GraphicsCaptureSessionFactory(ICaptureItemResolver captureItemResolver, Direct3D11DeviceProvider deviceProvider)
    {
        _captureItemResolver = captureItemResolver;
        _deviceProvider = deviceProvider;
    }

    public Task<OperationResult<ICaptureSession>> CreateAsync(
        CaptureSourceModel source,
        Func<CapturedFrame, OperationResult> frameProcessor,
        Action<AppError> runtimeErrorHandler,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var captureItemResult = _captureItemResolver.Resolve(source);
        if (!captureItemResult.IsSuccess || captureItemResult.Value is null)
        {
            return Task.FromResult(OperationResult<ICaptureSession>.Failure(captureItemResult.Error!));
        }

        var session = new CaptureSessionInstance(
            captureItemResult.Value,
            _deviceProvider.GetResources(),
            frameProcessor,
            runtimeErrorHandler);

        return Task.FromResult(OperationResult<ICaptureSession>.Success(session));
    }

    private sealed class CaptureSessionInstance : ICaptureSession
    {
        private readonly GraphicsCaptureItem _captureItem;
        private readonly D3D11DeviceResources _deviceResources;
        private readonly Func<CapturedFrame, OperationResult> _frameProcessor;
        private readonly Action<AppError> _runtimeErrorHandler;

        private Direct3D11CaptureFramePool? _framePool;
        private Windows.Graphics.Capture.GraphicsCaptureSession? _captureSession;
        private SizeInt32 _initialSize;
        private bool _isStarted;
        private bool _isStopping;
        private int _faulted;

        public CaptureSessionInstance(
            GraphicsCaptureItem captureItem,
            D3D11DeviceResources deviceResources,
            Func<CapturedFrame, OperationResult> frameProcessor,
            Action<AppError> runtimeErrorHandler)
        {
            _captureItem = captureItem;
            _deviceResources = deviceResources;
            _frameProcessor = frameProcessor;
            _runtimeErrorHandler = runtimeErrorHandler;
            _initialSize = captureItem.Size;
        }

        public nint NativeDevicePointer => _deviceResources.DevicePointer;

        public int Width => _initialSize.Width;

        public int Height => _initialSize.Height;

        public OperationResult Start()
        {
            if (_isStarted)
            {
                return OperationResult.Failure(AppError.InvalidState("Recording is already active."));
            }

            try
            {
                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _deviceResources.Direct3DDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _initialSize);

                _framePool.FrameArrived += OnFrameArrived;
                _captureSession = _framePool.CreateCaptureSession(_captureItem);
                _captureSession.StartCapture();
                _isStarted = true;
                return OperationResult.Success();
            }
            catch (Exception ex)
            {
                DisposeCore();
                return OperationResult.Failure(AppError.RecordingFailed($"ScreenFast could not start capture: {ex.Message}"));
            }
        }

        public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _isStopping = true;
            DisposeCore();
            return Task.FromResult(OperationResult.Success());
        }

        public ValueTask DisposeAsync()
        {
            DisposeCore();
            return ValueTask.CompletedTask;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (_isStopping)
            {
                return;
            }

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                if (frame.ContentSize.Width != _initialSize.Width || frame.ContentSize.Height != _initialSize.Height)
                {
                    ReportRuntimeError(AppError.SourceSizeChanged());
                    return;
                }

                var access = (IDirect3DDxgiInterfaceAccess)frame.Surface;
                var texturePointer = access.GetInterface(Direct3D11Native.Id3D11Texture2DGuid);

                try
                {
                    var frameResult = _frameProcessor(
                        new CapturedFrame(
                            texturePointer,
                            frame.SystemRelativeTime?.Ticks ?? 0,
                            frame.ContentSize.Width,
                            frame.ContentSize.Height));

                    if (!frameResult.IsSuccess && frameResult.Error is not null)
                    {
                        ReportRuntimeError(frameResult.Error);
                    }
                }
                finally
                {
                    if (texturePointer != nint.Zero)
                    {
                        System.Runtime.InteropServices.Marshal.Release(texturePointer);
                    }
                }
            }
            catch (Exception ex)
            {
                ReportRuntimeError(AppError.RecordingFailed($"ScreenFast capture failed while receiving frames: {ex.Message}"));
            }
        }

        private void ReportRuntimeError(AppError error)
        {
            if (Interlocked.Exchange(ref _faulted, 1) == 1)
            {
                return;
            }

            _isStopping = true;
            _runtimeErrorHandler(error);
        }

        private void DisposeCore()
        {
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
                _framePool.Dispose();
                _framePool = null;
            }

            _captureSession?.Dispose();
            _captureSession = null;
            _isStarted = false;
        }
    }
}
