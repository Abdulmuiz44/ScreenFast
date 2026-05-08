using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IPostRecordingProcessingPipeline
{
    Task<PostRecordingProcessingResult> ProcessSuccessfulRecordingAsync(PostRecordingProcessingRequest request, CancellationToken cancellationToken = default);
}
