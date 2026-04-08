using ScreenFast.Core.Results;

namespace ScreenFast.Core.Models;

public sealed record RecordingPreflightResult(bool CanStart, string? WarningMessage, AppError? Error)
{
    public static RecordingPreflightResult Success(string? warningMessage = null) => new(true, warningMessage, null);

    public static RecordingPreflightResult Failure(AppError error) => new(false, null, error);
}
