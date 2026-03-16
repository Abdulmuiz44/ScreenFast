using System.Globalization;
using ScreenFast.Capture.Interop;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Windows.Graphics.Capture;

namespace ScreenFast.Capture.Services;

public sealed class GraphicsCaptureItemResolver : ICaptureItemResolver
{
    public OperationResult<GraphicsCaptureItem> Resolve(CaptureSourceModel source)
    {
        if (string.IsNullOrWhiteSpace(source.SourceId))
        {
            return OperationResult<GraphicsCaptureItem>.Failure(
                AppError.SourceUnavailable("The selected source is missing an identifier. Select the source again."));
        }

        if (!TryParseHandle(source.SourceId, out var prefix, out var handle))
        {
            return OperationResult<GraphicsCaptureItem>.Failure(
                AppError.SourceUnavailable("The selected source identifier is invalid. Select the source again."));
        }

        try
        {
            GraphicsCaptureItem item = prefix switch
            {
                "window" when NativeMethods.IsWindow(handle) => GraphicsCaptureItemInterop.CreateForWindow(handle),
                "display" => GraphicsCaptureItemInterop.CreateForMonitor(handle),
                _ => throw new InvalidOperationException()
            };

            return OperationResult<GraphicsCaptureItem>.Success(item);
        }
        catch
        {
            return OperationResult<GraphicsCaptureItem>.Failure(
                AppError.SourceUnavailable("The selected source is no longer available. Select the display or window again."));
        }
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

        if (!long.TryParse(rawValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        handle = new nint(parsed);
        return true;
    }
}
