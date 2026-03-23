using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenFast.Capture.Interop;

public sealed class D3D11DeviceResources : IDisposable
{
    public D3D11DeviceResources(nint devicePointer, nint deviceContextPointer, nint dxgiDevicePointer, IDirect3DDevice direct3DDevice)
    {
        DevicePointer = devicePointer;
        DeviceContextPointer = deviceContextPointer;
        DxgiDevicePointer = dxgiDevicePointer;
        Direct3DDevice = direct3DDevice;
    }

    public nint DevicePointer { get; }

    public nint DeviceContextPointer { get; }

    public nint DxgiDevicePointer { get; }

    public IDirect3DDevice Direct3DDevice { get; }

    public void Dispose()
    {
        if (DxgiDevicePointer != nint.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(DxgiDevicePointer);
        }

        if (DeviceContextPointer != nint.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(DeviceContextPointer);
        }

        if (DevicePointer != nint.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(DevicePointer);
        }
    }
}
