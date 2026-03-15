using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Results;
using Windows.Graphics.Capture;
using WinRT.Interop;

namespace ScreenFast.Capture.Services;

public sealed class WindowsGraphicsCaptureSourcePickerService : ICaptureSourcePickerService
{
    private readonly GraphicsCaptureSourceResolver _resolver;

    public WindowsGraphicsCaptureSourcePickerService(GraphicsCaptureSourceResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<CaptureSourceSelectionResult> PickAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            return CaptureSourceSelectionResult.Failure(AppError.MissingWindowHandle());
        }

        try
        {
            var picker = new GraphicsCapturePicker();
            InitializeWithWindow.Initialize(picker, ownerWindowHandle);

            var selectedItem = await picker.PickSingleItemAsync();
            if (selectedItem is null)
            {
                return CaptureSourceSelectionResult.Cancelled();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return _resolver.Resolve(selectedItem);
        }
        catch (OperationCanceledException)
        {
            return CaptureSourceSelectionResult.Cancelled();
        }
        catch
        {
            return CaptureSourceSelectionResult.Failure(
                new AppError(
                    "source_picker_failed",
                    "ScreenFast could not open the source picker. Make sure the app has focus and try again."));
        }
    }
}
