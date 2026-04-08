using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingPreflightValidator : IRecordingPreflightValidator
{
    private const long HardMinimumBytes = 1L * 1024 * 1024 * 1024;
    private readonly IScreenFastLogService _logService;

    public RecordingPreflightValidator(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public Task<RecordingPreflightResult> ValidateAsync(RecorderStatusSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        RecordingPreflightResult result;
        if (snapshot.State is RecorderState.Recording or RecorderState.Stopping or RecorderState.Selecting)
        {
            result = RecordingPreflightResult.Failure(AppError.PreflightFailed("ScreenFast is busy right now. Wait a moment and try recording again."));
        }
        else if (snapshot.SelectedSource is null)
        {
            result = RecordingPreflightResult.Failure(AppError.PreflightFailed("Select a display or window before recording."));
        }
        else if (snapshot.SelectedSource.Width <= 0 || snapshot.SelectedSource.Height <= 0)
        {
            result = RecordingPreflightResult.Failure(AppError.PreflightFailed("The selected source dimensions are invalid. Re-select the source and try again."));
        }
        else if (string.IsNullOrWhiteSpace(snapshot.OutputFolder))
        {
            result = RecordingPreflightResult.Failure(AppError.PreflightFailed("Choose an output folder before recording."));
        }
        else if (!Directory.Exists(snapshot.OutputFolder))
        {
            result = RecordingPreflightResult.Failure(AppError.PreflightFailed("The selected output folder no longer exists. Choose it again before recording."));
        }
        else
        {
            try
            {
                var probePath = Path.Combine(snapshot.OutputFolder, $".screenfast-write-test-{Guid.NewGuid():N}.tmp");
                using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
                {
                }

                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }

                var freeSpaceCheck = ValidateFreeSpace(snapshot.OutputFolder, snapshot.QualityPreset);
                result = freeSpaceCheck;
            }
            catch
            {
                result = RecordingPreflightResult.Failure(AppError.PreflightFailed("ScreenFast cannot write to the selected output folder. Pick a writable folder and try again."));
            }
        }

        if (result.CanStart)
        {
            _logService.Info("preflight.passed", "Recording preflight validation succeeded.", new Dictionary<string, object?> { ["warning"] = result.WarningMessage });
        }
        else
        {
            _logService.Warning("preflight.failed", result.Error?.Message ?? "Recording preflight validation failed.");
        }

        return Task.FromResult(result);
    }

    private RecordingPreflightResult ValidateFreeSpace(string outputFolder, VideoQualityPreset preset)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(outputFolder));
            if (string.IsNullOrWhiteSpace(root))
            {
                return RecordingPreflightResult.Success();
            }

            var driveInfo = new DriveInfo(root);
            var freeBytes = driveInfo.AvailableFreeSpace;
            if (freeBytes < HardMinimumBytes)
            {
                return RecordingPreflightResult.Failure(AppError.PreflightFailed("The output drive is too low on free space. Free up at least 1 GB, then try again."));
            }

            var advisoryBytes = preset switch
            {
                VideoQualityPreset.Standard => 3L * 1024 * 1024 * 1024,
                VideoQualityPreset.High => 5L * 1024 * 1024 * 1024,
                _ => 8L * 1024 * 1024 * 1024
            };

            if (freeBytes < advisoryBytes)
            {
                return RecordingPreflightResult.Success($"Free space is getting low on this drive. {preset} recordings can consume space quickly.");
            }

            return RecordingPreflightResult.Success();
        }
        catch
        {
            return RecordingPreflightResult.Success();
        }
    }
}
