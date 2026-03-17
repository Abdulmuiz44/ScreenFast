using System.Diagnostics;
using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public sealed class FileLauncherService : IFileLauncherService
{
    private readonly IScreenFastLogService _logService;

    public FileLauncherService(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public Task<OperationResult> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = OpenPath(filePath, filePath, "ScreenFast could not open that file.");
        LogResult("shell.open_file", result, filePath);
        return Task.FromResult(result);
    }

    public Task<OperationResult> OpenContainingFolderAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var invalid = OperationResult.Failure(AppError.ShellActionFailed("There is no recording path to open."));
            LogResult("shell.open_folder", invalid, filePath);
            return Task.FromResult(invalid);
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
                var success = OperationResult.Success();
                LogResult("shell.open_folder", success, filePath);
                return Task.FromResult(success);
            }

            var directoryPath = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                var unavailable = OperationResult.Failure(AppError.ShellActionFailed("The recording folder is no longer available."));
                LogResult("shell.open_folder", unavailable, filePath);
                return Task.FromResult(unavailable);
            }

            var result = OpenPath(directoryPath, directoryPath, "ScreenFast could not open that folder.");
            LogResult("shell.open_folder", result, directoryPath);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            var failure = OperationResult.Failure(AppError.ShellActionFailed($"ScreenFast could not open that folder: {ex.Message}"));
            LogResult("shell.open_folder", failure, filePath);
            return Task.FromResult(failure);
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

    private void LogResult(string eventName, OperationResult result, string? path)
    {
        if (result.IsSuccess)
        {
            _logService.Info(eventName, "ScreenFast completed a shell open action.", new Dictionary<string, object?> { ["path"] = path });
        }
        else
        {
            _logService.Warning(eventName + "_failed", result.Error?.Message ?? "ScreenFast could not complete the shell open action.", new Dictionary<string, object?> { ["path"] = path });
        }
    }
}
