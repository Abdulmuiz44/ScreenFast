using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingMetadataSidecarService
{
    string GetSidecarPath(string videoFilePath);

    Task<OperationResult<string>> SaveAsync(RecordingSidecarMetadata metadata, CancellationToken cancellationToken = default);
}
