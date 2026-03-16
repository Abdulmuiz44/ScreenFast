using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public interface IFileLauncherService
{
    Task<OperationResult> OpenFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task<OperationResult> OpenContainingFolderAsync(string filePath, CancellationToken cancellationToken = default);
}
