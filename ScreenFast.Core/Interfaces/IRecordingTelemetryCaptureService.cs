using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingTelemetryCaptureService
{
    OperationResult<IRecordingTelemetrySession> Start(RecordingTelemetryStartRequest request);
}

public interface IRecordingTelemetrySession : IAsyncDisposable
{
    string SessionId { get; }

    void Pause(TimeSpan elapsed);

    void Resume(TimeSpan elapsed);

    Task<RecordingTelemetryTimeline> StopAsync(TimeSpan finalDuration, CancellationToken cancellationToken = default);
}
