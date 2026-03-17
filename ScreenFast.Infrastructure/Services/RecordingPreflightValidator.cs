using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingPreflightValidator : IRecordingPreflightValidator
{
    private readonly IScreenFastLogService _logService;

    public RecordingPreflightValidator(IScreenFastLogService logService)
    {
        _logService = logService;
    }

    public Task<OperationResult> ValidateAsync(RecorderStatusSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        OperationResult result;
        if (snapshot.State is RecorderState.Recording or RecorderState.Stopping or RecorderState.Selecting)
        {
            result = OperationResult.Failure(AppError.PreflightFailed("ScreenFast is busy right now. Wait a moment and try recording again."));
        }
        else if (snapshot.SelectedSource is null)
        {
            result = OperationResult.Failure(AppError.PreflightFailed("Select a display or window before recording."));
        }
        else if (snapshot.SelectedSource.Width <= 0 || snapshot.SelectedSource.Height <= 0)
        {
            result = OperationResult.Failure(AppError.PreflightFailed("The selected source dimensions are invalid. Re-select the source and try again."));
        }
        else if (string.IsNullOrWhiteSpace(snapshot.OutputFolder))
        {
            result = OperationResult.Failure(AppError.PreflightFailed("Choose an output folder before recording."));
        }
        else if (!Directory.Exists(snapshot.OutputFolder))
        {
            result = OperationResult.Failure(AppError.PreflightFailed("The selected output folder no longer exists. Choose it again before recording."));
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

                result = OperationResult.Success();
            }
            catch
            {
                result = OperationResult.Failure(AppError.PreflightFailed("ScreenFast cannot write to the selected output folder. Pick a writable folder and try again."));
            }
        }

        if (result.IsSuccess)
        {
            _logService.Info("preflight.passed", "Recording preflight validation succeeded.");
        }
        else
        {
            _logService.Warning("preflight.failed", result.Error?.Message ?? "Recording preflight validation failed.");
        }

        return Task.FromResult(result);
    }
}
