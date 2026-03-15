using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Results;

namespace ScreenFast.Infrastructure.Services;

public sealed class StubOutputFolderPickerService : IOutputFolderPickerService
{
    public Task<OperationResult<string>> PickOutputFolderAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            OperationResult<string>.Failure(
                new AppError(
                    "output_folder_not_implemented",
                    "Output folder selection is scaffolded but not wired yet. That will come with the recording pipeline slice.")));
    }
}
