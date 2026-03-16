using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenFast.App.Interop;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;
using Forms = System.Windows.Forms;

namespace ScreenFast.App.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private const int StartHotkeyId = 1;
    private const int StopHotkeyId = 2;
    private const int PauseResumeHotkeyId = 3;

    private readonly IRecorderOrchestrator _orchestrator;
    private readonly IAppPreferencesService _preferencesService;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _startMenuItem;
    private readonly Forms.ToolStripMenuItem _pauseResumeMenuItem;
    private readonly Forms.ToolStripMenuItem _stopMenuItem;
    private readonly Forms.ToolStripMenuItem _openMenuItem;
    private readonly Forms.ToolStripMenuItem _exitMenuItem;

    private DispatcherQueue? _dispatcherQueue;
    private NativeMethods.WindowProcedure? _windowProcedure;
    private nint _previousWindowProcedure;
    private nint _windowHandle;
    private bool _isInitialized;
    private bool _allowClose;
    private bool _isExitInProgress;
    private bool _isDisposed;
    private RecorderStatusSnapshot _snapshot;
    private HotkeySettings _activeHotkeys;

    public DesktopShellService(IRecorderOrchestrator orchestrator, IAppPreferencesService preferencesService)
    {
        _orchestrator = orchestrator;
        _preferencesService = preferencesService;
        _snapshot = orchestrator.Snapshot;
        _activeHotkeys = preferencesService.CurrentSettings.Hotkeys;
        _orchestrator.SnapshotChanged += OnSnapshotChanged;

        _menu = new Forms.ContextMenuStrip();
        _startMenuItem = new Forms.ToolStripMenuItem("Start Recording", null, async (_, _) => await _orchestrator.StartRecordingAsync());
        _pauseResumeMenuItem = new Forms.ToolStripMenuItem("Pause Recording", null, async (_, _) => await _orchestrator.TogglePauseResumeAsync());
        _stopMenuItem = new Forms.ToolStripMenuItem("Stop Recording", null, async (_, _) => await _orchestrator.StopRecordingAsync());
        _openMenuItem = new Forms.ToolStripMenuItem("Open ScreenFast", null, (_, _) => ShowWindow());
        _exitMenuItem = new Forms.ToolStripMenuItem("Exit", null, async (_, _) => await ExitAsync());

        _menu.Items.AddRange([
            _startMenuItem,
            _pauseResumeMenuItem,
            _stopMenuItem,
            new Forms.ToolStripSeparator(),
            _openMenuItem,
            _exitMenuItem
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            ContextMenuStrip = _menu,
            Text = "ScreenFast"
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public event EventHandler<string?>? MessageChanged;

    public void Initialize(nint windowHandle)
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _windowHandle = windowHandle;
        _windowProcedure = WindowProcedure;
        _previousWindowProcedure = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlpWndProc);
        var functionPointer = Marshal.GetFunctionPointerForDelegate(_windowProcedure);
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlpWndProc, functionPointer);
        _notifyIcon.Visible = true;
        _isInitialized = true;

        var registrationResult = RegisterHotkeys(_activeHotkeys, _activeHotkeys);
        if (!registrationResult.IsSuccess)
        {
            MessageChanged?.Invoke(this, registrationResult.Error?.Message);
        }

        ApplySnapshot(_snapshot);
    }

    public void ApplyStartupBehavior()
    {
        if (_preferencesService.CurrentSettings.LaunchMinimizedToTray)
        {
            HideWindow("ScreenFast started in the tray.");
        }
    }

    public async Task<OperationResult> UpdateHotkeysAsync(HotkeySettings hotkeys, CancellationToken cancellationToken = default)
    {
        var validation = ValidateHotkeys(hotkeys);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (_isInitialized)
        {
            var registration = RegisterHotkeys(hotkeys, _activeHotkeys);
            if (!registration.IsSuccess)
            {
                return registration;
            }
        }

        _activeHotkeys = hotkeys;
        return await _preferencesService.UpdateHotkeySettingsAsync(hotkeys, cancellationToken);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        UnregisterHotkeys();

        if (_windowHandle != nint.Zero && _previousWindowProcedure != nint.Zero)
        {
            NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlpWndProc, _previousWindowProcedure);
            _windowHandle = nint.Zero;
            _previousWindowProcedure = nint.Zero;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _orchestrator.SnapshotChanged -= OnSnapshotChanged;
    }

    private void OnSnapshotChanged(object? sender, RecorderStatusSnapshot snapshot)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(RecorderStatusSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        _snapshot = snapshot;
        _startMenuItem.Enabled = snapshot.State == RecorderState.Ready;
        _pauseResumeMenuItem.Enabled = snapshot.State is RecorderState.Recording or RecorderState.Paused;
        _pauseResumeMenuItem.Text = snapshot.State == RecorderState.Paused ? "Resume Recording" : "Pause Recording";
        _stopMenuItem.Enabled = snapshot.State is RecorderState.Recording or RecorderState.Paused;
        _notifyIcon.Text = BuildTrayText(snapshot);
    }

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmHotKey:
                _ = HandleHotkeyAsync(wParam.ToInt32());
                return 0;
            case NativeMethods.WmSize when wParam.ToInt32() == NativeMethods.SizeMinimized:
                if (_preferencesService.CurrentSettings.MinimizeToTray)
                {
                    HideWindow("ScreenFast minimized to the tray.");
                    return 0;
                }

                break;
            case NativeMethods.WmClose when !_allowClose:
                if (_snapshot.State is RecorderState.Recording or RecorderState.Paused || _preferencesService.CurrentSettings.CloseToTray)
                {
                    var statusMessage = _snapshot.State is RecorderState.Recording or RecorderState.Paused
                        ? "ScreenFast is still recording in the tray."
                        : "ScreenFast is still running in the tray.";
                    HideWindow(statusMessage);
                    return 0;
                }

                _allowClose = true;
                break;
        }

        return NativeMethods.CallWindowProc(_previousWindowProcedure, hwnd, message, wParam, lParam);
    }

    private async Task HandleHotkeyAsync(int hotkeyId)
    {
        try
        {
            switch (hotkeyId)
            {
                case StartHotkeyId:
                    await _orchestrator.StartRecordingAsync();
                    break;
                case StopHotkeyId:
                    await _orchestrator.StopRecordingAsync();
                    break;
                case PauseResumeHotkeyId:
                    await _orchestrator.TogglePauseResumeAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageChanged?.Invoke(this, $"ScreenFast could not process the hotkey command: {ex.Message}");
        }
    }

    private void HideWindow(string message)
    {
        if (_windowHandle == nint.Zero || _isDisposed)
        {
            return;
        }

        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
        MessageChanged?.Invoke(this, message);
    }

    private void ShowWindow()
    {
        if (_windowHandle == nint.Zero || _isDisposed)
        {
            return;
        }

        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(_windowHandle);
    }

    private async Task ExitAsync()
    {
        if (_isExitInProgress)
        {
            return;
        }

        _isExitInProgress = true;

        try
        {
            if (_snapshot.State is RecorderState.Recording or RecorderState.Paused)
            {
                MessageChanged?.Invoke(this, "Stopping the active recording before ScreenFast exits.");
                await _orchestrator.StopRecordingAsync();
            }

            _allowClose = true;
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _allowClose = false;
            _isExitInProgress = false;
            MessageChanged?.Invoke(this, $"ScreenFast could not exit cleanly: {ex.Message}");
        }
    }

    private OperationResult RegisterHotkeys(HotkeySettings requestedHotkeys, HotkeySettings fallbackHotkeys)
    {
        UnregisterHotkeys();

        var attempted = new List<(int Id, HotkeyGesture Gesture)>
        {
            (StartHotkeyId, requestedHotkeys.StartRecording),
            (StopHotkeyId, requestedHotkeys.StopRecording),
            (PauseResumeHotkeyId, requestedHotkeys.PauseResumeRecording)
        };

        var registered = new List<int>();
        foreach (var item in attempted)
        {
            if (!NativeMethods.RegisterHotKey(_windowHandle, item.Id, ToNativeModifiers(item.Gesture), (uint)item.Gesture.VirtualKey))
            {
                foreach (var hotkeyId in registered)
                {
                    NativeMethods.UnregisterHotKey(_windowHandle, hotkeyId);
                }

                if (!Equals(requestedHotkeys, fallbackHotkeys))
                {
                    RegisterHotkeys(fallbackHotkeys, fallbackHotkeys);
                }

                return OperationResult.Failure(
                    AppError.InvalidState($"The hotkey {item.Gesture.DisplayText} is already in use by another app. ScreenFast kept the previous shortcuts."));
            }

            registered.Add(item.Id);
        }

        return OperationResult.Success();
    }

    private void UnregisterHotkeys()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, StartHotkeyId);
        NativeMethods.UnregisterHotKey(_windowHandle, StopHotkeyId);
        NativeMethods.UnregisterHotKey(_windowHandle, PauseResumeHotkeyId);
    }

    private static OperationResult ValidateHotkeys(HotkeySettings hotkeys)
    {
        var gestures = new[]
        {
            hotkeys.StartRecording,
            hotkeys.StopRecording,
            hotkeys.PauseResumeRecording
        };

        if (gestures.Any(gesture => !gesture.HasAnyModifier))
        {
            return OperationResult.Failure(AppError.InvalidState("Each hotkey must include at least one modifier key."));
        }

        if (gestures.Any(gesture => gesture.VirtualKey is < 0x70 or > 0x87))
        {
            return OperationResult.Failure(AppError.InvalidState("ScreenFast hotkeys currently support function keys F1 through F24."));
        }

        if (gestures.Distinct().Count() != gestures.Length)
        {
            return OperationResult.Failure(AppError.InvalidState("Each ScreenFast hotkey must be unique."));
        }

        return OperationResult.Success();
    }

    private static uint ToNativeModifiers(HotkeyGesture gesture)
    {
        var modifiers = 0u;
        if (gesture.Control)
        {
            modifiers |= NativeMethods.ModControl;
        }

        if (gesture.Shift)
        {
            modifiers |= NativeMethods.ModShift;
        }

        if (gesture.Alt)
        {
            modifiers |= NativeMethods.ModAlt;
        }

        return modifiers;
    }

    private static string BuildTrayText(RecorderStatusSnapshot snapshot)
    {
        var text = $"ScreenFast: {snapshot.State}";
        return text.Length <= 63 ? text : text[..63];
    }
}
