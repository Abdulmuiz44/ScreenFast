using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IPostRecordingFileActionService
{
    Task<OperationResult> RunAsync(PostRecordingOpenBehavior behavior, string rawVideoPath, CancellationToken cancellationToken = default);
}
