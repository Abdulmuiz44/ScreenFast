using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingEncoderService
{
    event EventHandler<AppError>? RuntimeErrorOccurred;

    Task<OperationResult<RecordingSessionInfo>> StartAsync(RecordingStartRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<string>> StopAsync(CancellationToken cancellationToken = default);
}
