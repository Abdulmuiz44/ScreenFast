using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace ScreenFast.Capture.Interop;

internal static class Direct3D11Native
{
    private const uint D3D11SdkVersion = 7;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11CreateDeviceVideoSupport = 0x800;

    private static readonly Guid IdxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static readonly Guid Id3D11DeviceGuid = new("DB6F6DDB-AC77-4E88-8253-819DF9BBF140");
    public static readonly Guid Id3D11Texture2DGuid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    public static D3D11DeviceResources CreateDeviceResources()
    {
        var featureLevels = new[]
        {
            D3DFeatureLevel.Level11_1,
            D3DFeatureLevel.Level11_0,
            D3DFeatureLevel.Level10_1,
            D3DFeatureLevel.Level10_0
        };

        Marshal.ThrowExceptionForHR(
            D3D11CreateDevice(
                nint.Zero,
                D3DDriverType.Hardware,
                nint.Zero,
                D3D11CreateDeviceBgraSupport | D3D11CreateDeviceVideoSupport,
                featureLevels,
                (uint)featureLevels.Length,
                D3D11SdkVersion,
                out var devicePointer,
                out _,
                out var deviceContextPointer));

        nint dxgiDevicePointer = nint.Zero;
        nint inspectablePointer = nint.Zero;

        try
        {
            var dxgiGuid = IdxgiDeviceGuid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(devicePointer, ref dxgiGuid, out dxgiDevicePointer));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePointer, out inspectablePointer));

            var graphicsDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectablePointer);
            Marshal.Release(inspectablePointer);
            inspectablePointer = nint.Zero;

            return new D3D11DeviceResources(devicePointer, deviceContextPointer, dxgiDevicePointer, graphicsDevice);
        }
        catch
        {
            if (inspectablePointer != nint.Zero)
            {
                Marshal.Release(inspectablePointer);
            }

            if (dxgiDevicePointer != nint.Zero)
            {
                Marshal.Release(dxgiDevicePointer);
            }

            if (deviceContextPointer != nint.Zero)
            {
                Marshal.Release(deviceContextPointer);
            }

            if (devicePointer != nint.Zero)
            {
                Marshal.Release(devicePointer);
            }

            throw;
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        nint adapter,
        D3DDriverType driverType,
        nint software,
        uint flags,
        [In] D3DFeatureLevel[] featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint device,
        out D3DFeatureLevel featureLevel,
        out nint immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    internal enum D3DDriverType : uint
    {
        Hardware = 1
    }

    internal enum D3DFeatureLevel : uint
    {
        Level10_0 = 0xA000,
        Level10_1 = 0xA100,
        Level11_0 = 0xB000,
        Level11_1 = 0xB100
    }
}
