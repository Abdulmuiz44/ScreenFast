using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Windows.Graphics.Capture;

namespace ScreenFast.Core.Interfaces;

public interface ICaptureItemResolver
{
    void Remember(CaptureSourceModel source, GraphicsCaptureItem item);

    OperationResult<GraphicsCaptureItem> Resolve(CaptureSourceModel source);
}
