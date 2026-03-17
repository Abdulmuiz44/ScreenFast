using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Results;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenFast.Infrastructure.Services;

public sealed class OutputFolderPickerService : IOutputFolderPickerService
{
    private readonly IScreenFastLogService _logService;

    public OutputFolderPickerService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public async Task<OperationResult<string>> PickOutputFolderAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            _logService.Warning("folder_picker.missing_handle", "ScreenFast could not open the folder picker because the window handle was missing.");
            return OperationResult<string>.Failure(AppError.MissingWindowHandle());
        }

        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, ownerWindowHandle);

            var folder = await picker.PickSingleFolderAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (folder is null)
            {
                _logService.Info("folder_picker.cancelled", "Output folder selection was cancelled.");
                return OperationResult<string>.Success(string.Empty);
            }

            _logService.Info("folder_picker.selected", "ScreenFast selected an output folder.", new Dictionary<string, object?> { ["path"] = folder.Path });
            return OperationResult<string>.Success(folder.Path);
        }
        catch (OperationCanceledException)
        {
            _logService.Info("folder_picker.cancelled", "Output folder selection was cancelled.");
            return OperationResult<string>.Success(string.Empty);
        }
        catch (Exception ex)
        {
            _logService.Warning("folder_picker.failed", "ScreenFast could not open the folder picker.", new Dictionary<string, object?> { ["error"] = ex.Message });
            return OperationResult<string>.Failure(
                AppError.FolderSelectionFailed($"ScreenFast could not open the folder picker: {ex.Message}"));
        }
    }
}
