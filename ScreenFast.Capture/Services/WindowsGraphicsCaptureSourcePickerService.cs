using System.Runtime.InteropServices;
using System.Text;
using ScreenFast.Capture.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Forms = System.Windows.Forms;

namespace ScreenFast.Capture.Services;

public sealed class WindowsGraphicsCaptureSourcePickerService : ICaptureSourcePickerService
{
    private readonly ICaptureItemResolver _captureItemResolver;

    public WindowsGraphicsCaptureSourcePickerService(ICaptureItemResolver captureItemResolver)
    {
        _captureItemResolver = captureItemResolver;
    }

    public Task<CaptureSourceSelectionResult> PickAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            return Task.FromResult(CaptureSourceSelectionResult.Failure(AppError.MissingWindowHandle()));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sources = EnumerateSources(ownerWindowHandle).ToArray();
            if (sources.Length == 0)
            {
                return Task.FromResult(CaptureSourceSelectionResult.Failure(
                    AppError.SourceUnavailable("No displays or capturable windows are currently available.")));
            }

            using var dialog = new SourcePickerForm(sources);
            var owner = new Win32Window(ownerWindowHandle);
            var result = dialog.ShowDialog(owner);
            if (result != Forms.DialogResult.OK || dialog.SelectedSource is null)
            {
                return Task.FromResult(CaptureSourceSelectionResult.Cancelled());
            }

            _captureItemResolver.Remember(dialog.SelectedSource, dialog.SelectedItem);
            return Task.FromResult(CaptureSourceSelectionResult.Success(dialog.SelectedSource));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(CaptureSourceSelectionResult.Cancelled());
        }
        catch
        {
            return Task.FromResult(CaptureSourceSelectionResult.Failure(
                new AppError(
                    "source_picker_failed",
                    "ScreenFast could not open the source picker. Make sure the app has focus and try again.")));
        }
    }

    private static IEnumerable<(CaptureSourceModel Source, object NativeItem)> EnumerateCandidatePairs(nint ownerWindowHandle)
    {
        foreach (var display in EnumerateDisplays())
        {
            yield return display;
        }

        foreach (var window in EnumerateWindows(ownerWindowHandle))
        {
            yield return window;
        }
    }

    private static IEnumerable<SourceOption> EnumerateSources(nint ownerWindowHandle)
    {
        foreach (var pair in EnumerateCandidatePairs(ownerWindowHandle))
        {
            if (pair.NativeItem is Windows.Graphics.Capture.GraphicsCaptureItem item)
            {
                yield return new SourceOption(pair.Source, item);
            }
        }
    }

    private static IEnumerable<(CaptureSourceModel Source, Windows.Graphics.Capture.GraphicsCaptureItem NativeItem)> EnumerateDisplays()
    {
        var displays = new List<(CaptureSourceModel, Windows.Graphics.Capture.GraphicsCaptureItem)>();
        var displayIndex = 1;

        NativeMethods.EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            (nint monitorHandle, nint hdcMonitor, ref NativeMethods.Rect monitorRect, nint lParam) =>
            {
                var monitorInfo = new NativeMethods.MonitorInfoEx
                {
                    Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                    DeviceName = string.Empty
                };

                if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
                {
                    return true;
                }

                try
                {
                    var item = GraphicsCaptureItemInterop.CreateForMonitor(monitorHandle);
                    var width = item.Size.Width;
                    var height = item.Size.Height;
                    var name = $"Display {displayIndex}";
                    if (!string.IsNullOrWhiteSpace(monitorInfo.DeviceName))
                    {
                        name += $" ({monitorInfo.DeviceName})";
                    }

                    displays.Add((
                        new CaptureSourceModel(
                            $"display:0x{monitorHandle.ToInt64():X}",
                            CaptureSourceKind.Display,
                            name,
                            width,
                            height),
                        item));
                    displayIndex++;
                }
                catch
                {
                }

                return true;
            },
            nint.Zero);

        return displays;
    }

    private static IEnumerable<(CaptureSourceModel Source, Windows.Graphics.Capture.GraphicsCaptureItem NativeItem)> EnumerateWindows(nint ownerWindowHandle)
    {
        var windows = new List<(CaptureSourceModel, Windows.Graphics.Capture.GraphicsCaptureItem)>();
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows(
            (nint windowHandle, nint lParam) =>
            {
                if (windowHandle == shellWindow || windowHandle == ownerWindowHandle || !NativeMethods.IsWindowVisible(windowHandle))
                {
                    return true;
                }

                var titleLength = NativeMethods.GetWindowTextLength(windowHandle);
                if (titleLength <= 0)
                {
                    return true;
                }

                var exStyle = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64();
                if ((exStyle & NativeMethods.WsExToolWindow) == NativeMethods.WsExToolWindow)
                {
                    return true;
                }

                var titleBuilder = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                try
                {
                    var item = GraphicsCaptureItemInterop.CreateForWindow(windowHandle);
                    windows.Add((
                        new CaptureSourceModel(
                            $"window:0x{windowHandle.ToInt64():X}",
                            CaptureSourceKind.Window,
                            title,
                            item.Size.Width,
                            item.Size.Height),
                        item));
                }
                catch
                {
                }

                return true;
            },
            nint.Zero);

        return windows
            .OrderBy(window => window.Item1.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private sealed class Win32Window : Forms.IWin32Window
    {
        public Win32Window(nint handle)
        {
            Handle = handle;
        }

        public nint Handle { get; }
    }

    private sealed record SourceOption(CaptureSourceModel Source, Windows.Graphics.Capture.GraphicsCaptureItem NativeItem)
    {
        public override string ToString() => $"{Source.TypeDisplayName}: {Source.DisplayName} ({Source.Width} x {Source.Height})";
    }

    private sealed class SourcePickerForm : Forms.Form
    {
        private readonly Forms.ListBox _listBox;
        private readonly IReadOnlyList<SourceOption> _sources;

        public SourcePickerForm(IReadOnlyList<SourceOption> sources)
        {
            _sources = sources;
            Text = "Select Source";
            StartPosition = Forms.FormStartPosition.CenterParent;
            Width = 640;
            Height = 480;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            var description = new Forms.Label
            {
                Dock = Forms.DockStyle.Top,
                Height = 48,
                Text = "Choose a display or app window to record.",
                Padding = new Forms.Padding(12, 12, 12, 0)
            };

            _listBox = new Forms.ListBox
            {
                Dock = Forms.DockStyle.Fill,
                IntegralHeight = false,
                DataSource = _sources.ToList()
            };

            _listBox.DoubleClick += (_, _) => ConfirmSelection();

            var cancelButton = new Forms.Button
            {
                Text = "Cancel",
                DialogResult = Forms.DialogResult.Cancel,
                AutoSize = true
            };

            var selectButton = new Forms.Button
            {
                Text = "Select",
                AutoSize = true
            };
            selectButton.Click += (_, _) => ConfirmSelection();

            var buttons = new Forms.FlowLayoutPanel
            {
                Dock = Forms.DockStyle.Bottom,
                FlowDirection = Forms.FlowDirection.RightToLeft,
                Padding = new Forms.Padding(12),
                Height = 60
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(selectButton);

            Controls.Add(_listBox);
            Controls.Add(buttons);
            Controls.Add(description);

            AcceptButton = selectButton;
            CancelButton = cancelButton;
        }

        public CaptureSourceModel? SelectedSource { get; private set; }
        public Windows.Graphics.Capture.GraphicsCaptureItem? SelectedItem { get; private set; }

        private void ConfirmSelection()
        {
            if (_listBox.SelectedItem is not SourceOption selected)
            {
                return;
            }

            SelectedSource = selected.Source;
            SelectedItem = selected.NativeItem;
            DialogResult = Forms.DialogResult.OK;
            Close();
        }
    }
}
