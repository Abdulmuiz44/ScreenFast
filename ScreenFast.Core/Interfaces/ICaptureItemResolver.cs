using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using Windows.Graphics.Capture;

namespace ScreenFast.Core.Interfaces;

public interface ICaptureItemResolver
{
    OperationResult<GraphicsCaptureItem> Resolve(CaptureSourceModel source);
}
