using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Results;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ScreenFast.Infrastructure.Services;

public sealed class OutputFolderPickerService : IOutputFolderPickerService
{
    public async Task<OperationResult<string>> PickOutputFolderAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        if (ownerWindowHandle == nint.Zero)
        {
            return OperationResult<string>.Failure(AppError.MissingWindowHandle());
        }

        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, ownerWindowHandle);

            var folder = await picker.PickSingleFolderAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return folder is null
                ? OperationResult<string>.Success(string.Empty)
                : OperationResult<string>.Success(folder.Path);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Success(string.Empty);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Failure(
                AppError.FolderSelectionFailed($"ScreenFast could not open the folder picker: {ex.Message}"));
        }
    }
}
