using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface ICaptureSessionFactory
{
    Task<OperationResult<ICaptureSession>> CreateAsync(
        CaptureSourceModel source,
        Func<CapturedFrame, OperationResult> frameProcessor,
        Action<AppError> runtimeErrorHandler,
        CancellationToken cancellationToken = default);
}
