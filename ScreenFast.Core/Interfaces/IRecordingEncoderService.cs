using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingEncoderService
{
    Task<OperationResult> StartAsync(RecordingStartRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
}
