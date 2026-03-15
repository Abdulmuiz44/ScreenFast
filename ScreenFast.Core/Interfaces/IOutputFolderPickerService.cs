using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IOutputFolderPickerService
{
    Task<OperationResult<string>> PickOutputFolderAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default);
}
