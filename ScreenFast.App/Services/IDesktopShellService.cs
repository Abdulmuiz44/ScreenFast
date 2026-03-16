namespace ScreenFast.App.Services;

public interface IDesktopShellService : IDisposable
{
    event EventHandler<string?>? MessageChanged;

    void Initialize(nint windowHandle);
}
