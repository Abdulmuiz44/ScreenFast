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
        var exactMatch = TryResolveByComIdentity(selectedItem);
        if (exactMatch is not null)
        {
            return CaptureSourceSelectionResult.Success(exactMatch);
        }

        var fallbackMatch = TryResolveByMetadata(selectedItem);
        if (fallbackMatch is not null)
        {
            return CaptureSourceSelectionResult.Success(fallbackMatch);
        }

        return CaptureSourceSelectionResult.Failure(
            new AppError(
                "source_resolution_failed",
                "Windows returned a capture source, but ScreenFast could not identify whether it was a window or display. Try selecting a different source."));
    }

    private CaptureSourceModel? TryResolveByComIdentity(GraphicsCaptureItem selectedItem)
    {
        var selectedIdentity = GetIdentityPointer(selectedItem);
        if (selectedIdentity == nint.Zero)
        {
            return null;
        }

        try
        {
            foreach (var candidate in EnumerateCandidates())
            {
                var candidateItem = CreateCandidateItem(candidate);
                if (candidateItem is null)
                {
                    continue;
                }

                var candidateIdentity = GetIdentityPointer(candidateItem);
                if (candidateIdentity == nint.Zero)
                {
                    continue;
                }

                try
                {
                    if (candidateIdentity == selectedIdentity)
                    {
                        return candidate.ToModel(selectedItem.DisplayName, selectedItem.Size.Width, selectedItem.Size.Height);
                    }
                }
                finally
                {
                    Marshal.Release(candidateIdentity);
                }
            }
        }
        finally
        {
            Marshal.Release(selectedIdentity);
        }

        return null;
    }

    private CaptureSourceModel? TryResolveByMetadata(GraphicsCaptureItem selectedItem)
    {
        var matches = EnumerateCandidates()
            .Where(candidate => candidate.Width == selectedItem.Size.Width && candidate.Height == selectedItem.Size.Height)
            .Where(candidate =>
                string.Equals(candidate.DisplayName, selectedItem.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(selectedItem.DisplayName))
            .Take(2)
            .ToArray();

        return matches.Length == 1
            ? matches[0].ToModel(selectedItem.DisplayName, selectedItem.Size.Width, selectedItem.Size.Height)
            : null;
    }

    private static nint GetIdentityPointer(object obj)
    {
        try
        {
            return Marshal.GetIUnknownForObject(obj);
        }
        catch
        {
            return nint.Zero;
        }
    }

    private static GraphicsCaptureItem? CreateCandidateItem(CaptureSourceCandidate candidate)
    {
        try
        {
            return candidate.Kind == CaptureSourceKind.Window
                ? GraphicsCaptureItemInterop.CreateForWindow(candidate.Handle)
                : GraphicsCaptureItemInterop.CreateForMonitor(candidate.Handle);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<CaptureSourceCandidate> EnumerateCandidates()
    {
        foreach (var display in EnumerateDisplays())
        {
            yield return display;
        }

        foreach (var window in EnumerateWindows())
        {
            yield return window;
        }
    }

    private static IEnumerable<CaptureSourceCandidate> EnumerateDisplays()
    {
        var displays = new List<CaptureSourceCandidate>();
        NativeMethods.EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            (monitorHandle, _, ref NativeMethods.Rect monitorRect, _) =>
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
            (windowHandle, _) =>
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

                try
                {
                    var item = GraphicsCaptureItemInterop.CreateForWindow(windowHandle);
                    windows.Add(
                        new CaptureSourceCandidate(
                            CaptureSourceKind.Window,
                            windowHandle,
                            titleBuilder.ToString(),
                            item.Size.Width,
                            item.Size.Height));
                }
                catch
                {
                    // Skip windows that Windows.Graphics.Capture will not accept.
                }

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
