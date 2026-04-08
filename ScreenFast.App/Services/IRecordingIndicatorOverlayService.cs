using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public interface IRecordingIndicatorOverlayService : IDisposable
{
    void Initialize(nint ownerWindowHandle);
}
