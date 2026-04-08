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
    private readonly IScreenFastLogService _logService;

    public WindowsGraphicsCaptureSourcePickerService(
        ICaptureItemResolver captureItemResolver,
        IScreenFastLogService logService)
    {
        _captureItemResolver = captureItemResolver;
        _logService = logService;
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
            _logService.Info("source_picker.enumerating", "ScreenFast is enumerating available capture sources.");
            var sources = EnumerateSources(ownerWindowHandle).ToArray();
            _logService.Info(
                "source_picker.enumerated",
                "ScreenFast enumerated available capture sources.",
                new Dictionary<string, object?> { ["count"] = sources.Length });
            if (sources.Length == 0)
            {
                return Task.FromResult(CaptureSourceSelectionResult.Failure(
                    AppError.SourceUnavailable("No displays or capturable windows are currently available.")));
            }

            using var dialog = new SourcePickerForm(sources);
            _logService.Info("source_picker.dialog_opening", "ScreenFast is opening the custom source picker dialog.");
            var result = dialog.ShowDialog();
            _logService.Info(
                "source_picker.dialog_closed",
                "ScreenFast closed the custom source picker dialog.",
                new Dictionary<string, object?> { ["result"] = result.ToString() });
            if (result != Forms.DialogResult.OK || dialog.SelectedSource is null)
            {
                return Task.FromResult(CaptureSourceSelectionResult.Cancelled());
            }

            _logService.Info(
                "source_picker.creating_item",
                "ScreenFast is creating a capture item for the selected source.",
                new Dictionary<string, object?>
                {
                    ["sourceId"] = dialog.SelectedSource.SourceId,
                    ["sourceType"] = dialog.SelectedSource.Type.ToString(),
                    ["displayName"] = dialog.SelectedSource.DisplayName
                });
            var selectedItem = CreateCaptureItem(dialog.SelectedSource);
            _captureItemResolver.Remember(dialog.SelectedSource, selectedItem);
            return Task.FromResult(CaptureSourceSelectionResult.Success(dialog.SelectedSource));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(CaptureSourceSelectionResult.Cancelled());
        }
        catch
        {
            _logService.Error("source_picker.failed", "ScreenFast failed while opening or handling the source picker dialog.");
            return Task.FromResult(CaptureSourceSelectionResult.Failure(
                new AppError(
                    "source_picker_failed",
                    "ScreenFast could not open the source picker. Make sure the app has focus and try again.")));
        }
    }

    private static Windows.Graphics.Capture.GraphicsCaptureItem CreateCaptureItem(CaptureSourceModel source)
    {
        if (!TryParseHandle(source.SourceId, out var prefix, out var handle) || handle == nint.Zero)
        {
            throw new InvalidOperationException("The selected source handle is invalid.");
        }

        return prefix switch
        {
            "window" => GraphicsCaptureItemInterop.CreateForWindow(handle),
            "display" => GraphicsCaptureItemInterop.CreateForMonitor(handle),
            _ => throw new InvalidOperationException("The selected source type is not supported.")
        };
    }

    private static bool TryParseHandle(string sourceId, out string prefix, out nint handle)
    {
        prefix = string.Empty;
        handle = nint.Zero;

        var parts = sourceId.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        prefix = parts[0].ToLowerInvariant();
        var rawValue = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? parts[1][2..]
            : parts[1];

        if (!long.TryParse(rawValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        handle = new nint(parsed);
        return true;
    }

    private static IEnumerable<CaptureSourceModel> EnumerateCandidatePairs(nint ownerWindowHandle)
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
        return EnumerateCandidatePairs(ownerWindowHandle).Select(source => new SourceOption(source));
    }

    private static IEnumerable<CaptureSourceModel> EnumerateDisplays()
    {
        var displays = new List<CaptureSourceModel>();
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

                var width = Math.Max(0, monitorRect.Right - monitorRect.Left);
                var height = Math.Max(0, monitorRect.Bottom - monitorRect.Top);
                if (width == 0 || height == 0)
                {
                    return true;
                }

                var name = $"Display {displayIndex}";
                if (!string.IsNullOrWhiteSpace(monitorInfo.DeviceName))
                {
                    name += $" ({monitorInfo.DeviceName})";
                }

                displays.Add(
                    new CaptureSourceModel(
                        $"display:0x{monitorHandle.ToInt64():X}",
                        CaptureSourceKind.Display,
                        name,
                        width,
                        height));
                displayIndex++;

                return true;
            },
            nint.Zero);

        return displays.OrderBy(display => display.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IEnumerable<CaptureSourceModel> EnumerateWindows(nint ownerWindowHandle)
    {
        var windows = new List<CaptureSourceModel>();
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

                if (!NativeMethods.GetWindowRect(windowHandle, out var windowRect))
                {
                    return true;
                }

                var width = Math.Max(0, windowRect.Right - windowRect.Left);
                var height = Math.Max(0, windowRect.Bottom - windowRect.Top);
                if (width == 0 || height == 0)
                {
                    return true;
                }

                windows.Add(
                    new CaptureSourceModel(
                        $"window:0x{windowHandle.ToInt64():X}",
                        CaptureSourceKind.Window,
                        title,
                        width,
                        height));

                return true;
            },
            nint.Zero);

        return windows
            .OrderBy(window => window.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private sealed record SourceOption(CaptureSourceModel Source)
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
            TopMost = true;

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
        private void ConfirmSelection()
        {
            if (_listBox.SelectedItem is not SourceOption selected)
            {
                return;
            }

            SelectedSource = selected.Source;
            DialogResult = Forms.DialogResult.OK;
            Close();
        }
    }
}
