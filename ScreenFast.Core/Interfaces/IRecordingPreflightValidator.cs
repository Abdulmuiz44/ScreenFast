using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.Core.Interfaces;

public interface IRecordingPreflightValidator
{
    Task<OperationResult> ValidateAsync(RecorderStatusSnapshot snapshot, CancellationToken cancellationToken = default);
}
