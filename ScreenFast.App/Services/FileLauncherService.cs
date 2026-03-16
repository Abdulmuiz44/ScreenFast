using System.Diagnostics;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public sealed class FileLauncherService : IFileLauncherService
{
    public Task<OperationResult> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OpenPath(filePath, filePath, "ScreenFast could not open that file."));
    }

    public Task<OperationResult> OpenContainingFolderAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(OperationResult.Failure(AppError.ShellActionFailed("There is no recording path to open.")));
        }

        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
                return Task.FromResult(OperationResult.Success());
            }

            var directoryPath = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return Task.FromResult(OperationResult.Failure(AppError.ShellActionFailed("The recording folder is no longer available.")));
            }

            return Task.FromResult(OpenPath(directoryPath, directoryPath, "ScreenFast could not open that folder."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Failure(AppError.ShellActionFailed($"ScreenFast could not open that folder: {ex.Message}")));
        }
    }

    private static OperationResult OpenPath(string pathToOpen, string existencePath, string fallbackMessage)
    {
        if (string.IsNullOrWhiteSpace(existencePath) || (!File.Exists(existencePath) && !Directory.Exists(existencePath)))
        {
            return OperationResult.Failure(AppError.ShellActionFailed("That item is no longer available on disk."));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pathToOpen,
                UseShellExecute = true
            });
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(AppError.ShellActionFailed($"{fallbackMessage} {ex.Message}"));
        }
    }
}
