using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingMetadataReader
{
    Task<OperationResult<RecordingRenderInput>> ReadAsync(string metadataPath, CancellationToken cancellationToken = default);

    Task<DiagnosticsMetadataSidecarSummary> CreateDiagnosticsSummaryAsync(string videoFilePath, CancellationToken cancellationToken = default);
}
