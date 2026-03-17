using ScreenFast.Core.Models;
using ScreenFast.Core.Results;

namespace ScreenFast.App.Services;

public interface IDesktopShellService : IDisposable
{
    event EventHandler<string?>? MessageChanged;

    void Initialize(nint windowHandle);

    void ApplyStartupBehavior();

    Task<OperationResult> UpdateHotkeysAsync(HotkeySettings hotkeys, CancellationToken cancellationToken = default);
}
