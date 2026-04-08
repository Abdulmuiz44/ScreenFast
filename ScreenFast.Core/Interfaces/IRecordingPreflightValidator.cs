using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingPreflightValidator
{
    Task<RecordingPreflightResult> ValidateAsync(RecorderStatusSnapshot snapshot, CancellationToken cancellationToken = default);
}
