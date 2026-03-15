using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;

namespace ScreenFast.Capture.Interop;

internal static class GraphicsCaptureItemInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = typeof(GraphicsCaptureItem).GUID;

    public static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        var factory = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        var itemPointer = factory.CreateForWindow(windowHandle, GraphicsCaptureItemGuid);

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    public static GraphicsCaptureItem CreateForMonitor(nint monitorHandle)
    {
        var factory = (IGraphicsCaptureItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        var itemPointer = factory.CreateForMonitor(monitorHandle, GraphicsCaptureItemGuid);

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(nint window, in Guid iid);

        IntPtr CreateForMonitor(nint monitor, in Guid iid);
    }
}
