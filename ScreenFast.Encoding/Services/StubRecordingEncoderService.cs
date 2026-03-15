using ScreenFast.Core.Interfaces;
using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Encoding.Services;

public sealed class StubRecordingEncoderService : IRecordingEncoderService
{
    public Task<OperationResult> StartAsync(RecordingStartRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            OperationResult.Failure(
                new AppError(
                    "recording_not_implemented",
                    "MP4 recording is not implemented yet. Source selection is ready, but capture and encoding still need to be wired.")));
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.Success());
    }
}
