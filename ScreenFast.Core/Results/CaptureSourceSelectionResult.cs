using ScreenFast.Core.Models;

namespace ScreenFast.Core.Results;

public enum CaptureSourceSelectionStatus
{
    Success = 0,
    Cancelled = 1,
    Failed = 2
}

public sealed record CaptureSourceSelectionResult(
    CaptureSourceSelectionStatus Status,
    CaptureSourceModel? Source,
    AppError? Error)
{
    public bool IsSuccess => Status == CaptureSourceSelectionStatus.Success && Source is not null;

    public static CaptureSourceSelectionResult Success(CaptureSourceModel source) =>
        new(CaptureSourceSelectionStatus.Success, source, null);

    public static CaptureSourceSelectionResult Cancelled() =>
        new(CaptureSourceSelectionStatus.Cancelled, null, null);

    public static CaptureSourceSelectionResult Failure(AppError error) =>
        new(CaptureSourceSelectionStatus.Failed, null, error);
}
