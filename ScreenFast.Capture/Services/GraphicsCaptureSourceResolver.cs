using System.Runtime.InteropServices;
using System.Text;
using ScreenFast.Capture.Interop;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Windows.Graphics.Capture;

namespace ScreenFast.Capture.Services;

public sealed class GraphicsCaptureSourceResolver
{
    public CaptureSourceSelectionResult Resolve(GraphicsCaptureItem selectedItem)
    {
        var resolvedMatch = TryResolveByMetadata(selectedItem);
        if (resolvedMatch is not null)
        {
            return CaptureSourceSelectionResult.Success(resolvedMatch);
        }

        return CaptureSourceSelectionResult.Failure(
            new AppError(
                "source_resolution_failed",
                "Windows returned a capture source, but ScreenFast could not identify whether it was a window or display. Try selecting a different source."));
    }

    private CaptureSourceModel? TryResolveByMetadata(GraphicsCaptureItem selectedItem)
    {
        var selectedName = selectedItem.DisplayName?.Trim() ?? string.Empty;
        var width = selectedItem.Size.Width;
        var height = selectedItem.Size.Height;

        var matchingDisplays = FindMatches(EnumerateDisplays(), selectedName, width, height);
        if (matchingDisplays.Length == 1)
        {
            return matchingDisplays[0].ToModel(selectedName, width, height);
        }

        var matchingWindows = FindMatches(EnumerateWindows(), selectedName, width, height);
        if (matchingWindows.Length == 1)
        {
            return matchingWindows[0].ToModel(selectedName, width, height);
        }

        var fallbackMatches = matchingDisplays.Concat(matchingWindows).Take(2).ToArray();
        return fallbackMatches.Length == 1
            ? fallbackMatches[0].ToModel(selectedName, width, height)
            : null;
    }

    private static CaptureSourceCandidate[] FindMatches(
        IEnumerable<CaptureSourceCandidate> candidates,
        string selectedName,
        int width,
        int height)
    {
        var sizeMatches = candidates
            .Where(candidate => candidate.Width == width && candidate.Height == height)
            .Take(8)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            var namedMatches = sizeMatches
                .Where(candidate => string.Equals(candidate.DisplayName, selectedName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();

            if (namedMatches.Length > 0)
            {
                return namedMatches;
            }
        }

        return sizeMatches.Take(2).ToArray();
    }

    private static IEnumerable<CaptureSourceCandidate> EnumerateDisplays()
    {
        var displays = new List<CaptureSourceCandidate>();
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

                displays.Add(
                    new CaptureSourceCandidate(
                        CaptureSourceKind.Display,
                        monitorHandle,
                        string.IsNullOrWhiteSpace(monitorInfo.DeviceName) ? "Display" : monitorInfo.DeviceName,
                        monitorRect.Right - monitorRect.Left,
                        monitorRect.Bottom - monitorRect.Top));

                return true;
            },
            nint.Zero);

        return displays;
    }

    private static IEnumerable<CaptureSourceCandidate> EnumerateWindows()
    {
        var windows = new List<CaptureSourceCandidate>();
        var shellWindow = NativeMethods.GetShellWindow();

        NativeMethods.EnumWindows(
            (nint windowHandle, nint lParam) =>
            {
                if (windowHandle == shellWindow || !NativeMethods.IsWindowVisible(windowHandle))
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
                    new CaptureSourceCandidate(
                        CaptureSourceKind.Window,
                        windowHandle,
                        titleBuilder.ToString(),
                        width,
                        height));

                return true;
            },
            nint.Zero);

        return windows;
    }

    private sealed record CaptureSourceCandidate(
        CaptureSourceKind Kind,
        nint Handle,
        string DisplayName,
        int Width,
        int Height)
    {
        public CaptureSourceModel ToModel(string selectedDisplayName, int width, int height)
        {
            var name = string.IsNullOrWhiteSpace(selectedDisplayName) ? DisplayName : selectedDisplayName;
            var sourceId = Kind == CaptureSourceKind.Window
                ? $"window:0x{Handle.ToInt64():X}"
                : $"display:0x{Handle.ToInt64():X}";

            return new CaptureSourceModel(sourceId, Kind, name, width, height);
        }
    }
}

