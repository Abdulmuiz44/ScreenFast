using ScreenFast.Core.Models;

namespace ScreenFast.Core.Interfaces;

public interface IAppSmokeCheckService
{
    SmokeCheckReport? CurrentReport { get; }

    Task<SmokeCheckReport> RunAsync(nint ownerWindowHandle, CancellationToken cancellationToken = default);
}
