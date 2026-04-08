using Microsoft.UI.Dispatching;
using ScreenFast.App.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.State;
using WinRT.Interop;

namespace ScreenFast.App.Services;

public sealed class RecordingIndicatorOverlayService : IRecordingIndicatorOverlayService
{
    private readonly IRecorderOrchestrator _orchestrator;
    private readonly IScreenFastLogService _logService;
    private RecordingIndicatorOverlayWindow? _overlayWindow;
    private DispatcherQueue? _dispatcherQueue;
    private nint _ownerWindowHandle;
    private bool _overlayFaulted;
    private bool _isDisposed;

    public RecordingIndicatorOverlayService(IRecorderOrchestrator orchestrator, IScreenFastLogService logService)
    {
        _orchestrator = orchestrator;
        _logService = logService;
        _orchestrator.SnapshotChanged += OnSnapshotChanged;
    }

    public void Initialize(nint ownerWindowHandle)
    {
        _ownerWindowHandle = ownerWindowHandle;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Dispose()
    {
        _isDisposed = true;

        try
        {
            _overlayWindow?.Close();
        }
        catch
        {
        }

        _orchestrator.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        if (_isDisposed || _overlayFaulted)
        {
            return;
        }

        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(RecorderStatusSnapshot snapshot)
    {
        try
        {
            if (!snapshot.OverlayEnabled || _ownerWindowHandle == nint.Zero)
            {
                HideOverlay();
                return;
            }

            if (snapshot.State is RecorderState.Recording or RecorderState.Paused)
            {
                EnsureOverlay();
                _overlayWindow?.Update(snapshot.State == RecorderState.Paused ? "Paused" : "Recording", snapshot.TimerText);
                return;
            }

            HideOverlay();
        }
        catch (Exception ex)
        {
            _overlayFaulted = true;
            HideOverlay();
            _logService.Warning("overlay.failed", $"ScreenFast could not show the recording overlay: {ex.Message}");
        }
    }

    private void EnsureOverlay()
    {
        if (_overlayWindow is not null)
        {
            return;
        }

        _overlayWindow = new RecordingIndicatorOverlayWindow();
        _overlayWindow.Activate();
        _overlayWindow.Configure(_ownerWindowHandle);
        NativeMethods.ShowWindow(WindowNative.GetWindowHandle(_overlayWindow), NativeMethods.SwRestore);
        _logService.Info("overlay.shown", "ScreenFast showed the recording overlay.");
    }

    private void HideOverlay()
    {
        if (_overlayWindow is null)
        {
            return;
        }

        try
        {
            _overlayWindow.Close();
        }
        catch
        {
        }
        finally
        {
            _overlayWindow = null;
        }
    }
}



