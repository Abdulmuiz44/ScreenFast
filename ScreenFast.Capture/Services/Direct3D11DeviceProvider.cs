using ScreenFast.Capture.Interop;

namespace ScreenFast.Capture.Services;

public sealed class Direct3D11DeviceProvider : IDisposable
{
    private readonly Lazy<D3D11DeviceResources> _resources;

    public Direct3D11DeviceProvider()
    {
        _resources = new Lazy<D3D11DeviceResources>(Direct3D11Native.CreateDeviceResources, true);
    }

    public D3D11DeviceResources GetResources() => _resources.Value;

    public void Dispose()
    {
        if (_resources.IsValueCreated)
        {
            _resources.Value.Dispose();
        }
    }
}
