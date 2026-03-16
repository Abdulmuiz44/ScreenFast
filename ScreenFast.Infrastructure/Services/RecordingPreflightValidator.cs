using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;
using ScreenFast.Core.State;

namespace ScreenFast.Infrastructure.Services;

public sealed class RecordingPreflightValidator : IRecordingPreflightValidator
{
    public Task<OperationResult> ValidateAsync(RecorderStatusSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.State is RecorderState.Recording or RecorderState.Stopping or RecorderState.Selecting)
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("ScreenFast is busy right now. Wait a moment and try recording again.")));
        }

        if (snapshot.SelectedSource is null)
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("Select a display or window before recording.")));
        }

        if (snapshot.SelectedSource.Width <= 0 || snapshot.SelectedSource.Height <= 0)
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("The selected source dimensions are invalid. Re-select the source and try again.")));
        }

        if (string.IsNullOrWhiteSpace(snapshot.OutputFolder))
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("Choose an output folder before recording.")));
        }

        if (!Directory.Exists(snapshot.OutputFolder))
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("The selected output folder no longer exists. Choose it again before recording.")));
        }

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

            return Task.FromResult(OperationResult.Success());
        }
        catch
        {
            return Task.FromResult(OperationResult.Failure(AppError.PreflightFailed("ScreenFast cannot write to the selected output folder. Pick a writable folder and try again.")));
        }
    }
}
