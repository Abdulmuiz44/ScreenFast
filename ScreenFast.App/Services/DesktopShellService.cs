using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ScreenFast.App.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.State;
using Forms = System.Windows.Forms;

namespace ScreenFast.App.Services;

public sealed class DesktopShellService : IDesktopShellService
{
    private static readonly HotkeyDefinition[] Hotkeys =
    [
        new(1, "Start recording", "Ctrl+Shift+F9", NativeMethods.ModControl | NativeMethods.ModShift, 0x78),
        new(2, "Stop recording", "Ctrl+Shift+F10", NativeMethods.ModControl | NativeMethods.ModShift, 0x79),
        new(3, "Pause or resume", "Ctrl+Shift+F11", NativeMethods.ModControl | NativeMethods.ModShift, 0x7A)
    ];

    private readonly IRecorderOrchestrator _orchestrator;
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

    public DesktopShellService(IRecorderOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _snapshot = orchestrator.Snapshot;
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
        var functionPointer = Marshal.GetFunctionPointerForDelegate(_windowProcedure);
        _previousWindowProcedure = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlpWndProc, functionPointer);
        _notifyIcon.Visible = true;
        _isInitialized = true;

        RegisterHotkeys();
        ApplySnapshot(_snapshot);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_windowHandle != nint.Zero)
        {
            foreach (var hotkey in Hotkeys)
            {
                NativeMethods.UnregisterHotKey(_windowHandle, hotkey.Id);
            }

            if (_previousWindowProcedure != nint.Zero)
            {
                NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlpWndProc, _previousWindowProcedure);
            }

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

    private void RegisterHotkeys()
    {
        var warnings = new List<string>();
        foreach (var hotkey in Hotkeys)
        {
            if (!NativeMethods.RegisterHotKey(_windowHandle, hotkey.Id, hotkey.Modifiers, hotkey.VirtualKey))
            {
                warnings.Add($"{hotkey.Description} hotkey ({hotkey.DisplayText}) is unavailable because another app is already using it.");
            }
        }

        if (warnings.Count > 0)
        {
            MessageChanged?.Invoke(this, string.Join(" ", warnings));
        }
    }

    private nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmHotKey:
                _ = HandleHotkeyAsync(wParam.ToInt32());
                return 0;
            case NativeMethods.WmSize when wParam.ToInt32() == NativeMethods.SizeMinimized:
                HideWindow("ScreenFast minimized to the tray.");
                return 0;
            case NativeMethods.WmClose when !_allowClose:
                var statusMessage = _snapshot.State is RecorderState.Recording or RecorderState.Paused
                    ? "ScreenFast is still recording in the tray."
                    : "ScreenFast is still running in the tray.";
                HideWindow(statusMessage);
                return 0;
            default:
                return NativeMethods.CallWindowProc(_previousWindowProcedure, hwnd, message, wParam, lParam);
        }
    }

    private async Task HandleHotkeyAsync(int hotkeyId)
    {
        try
        {
            switch (hotkeyId)
            {
                case 1:
                    await _orchestrator.StartRecordingAsync();
                    break;
                case 2:
                    await _orchestrator.StopRecordingAsync();
                    break;
                case 3:
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

    private static string BuildTrayText(RecorderStatusSnapshot snapshot)
    {
        var text = $"ScreenFast: {snapshot.State}";
        return text.Length <= 63 ? text : text[..63];
    }

    private sealed record HotkeyDefinition(int Id, string Description, string DisplayText, uint Modifiers, uint VirtualKey);
}
