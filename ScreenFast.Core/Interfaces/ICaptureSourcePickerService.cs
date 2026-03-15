using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface ICaptureSourcePickerService
{
    Task<CaptureSourceSelectionResult> PickAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default);
}
