using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace ScreenFast.Capture.Interop;

internal static class GraphicsCaptureItemInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = typeof(GraphicsCaptureItem).GUID;

    public static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        var factory = GetActivationFactory<IGraphicsCaptureItemInterop>(typeof(GraphicsCaptureItem).FullName!);
        var hr = factory.CreateForWindow(windowHandle, GraphicsCaptureItemGuid, out var itemPointer);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            if (itemPointer != nint.Zero)
            {
                Marshal.Release(itemPointer);
            }
        }
    }

    public static GraphicsCaptureItem CreateForMonitor(nint monitorHandle)
    {
        var factory = GetActivationFactory<IGraphicsCaptureItemInterop>(typeof(GraphicsCaptureItem).FullName!);
        var hr = factory.CreateForMonitor(monitorHandle, GraphicsCaptureItemGuid, out var itemPointer);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            if (itemPointer != nint.Zero)
            {
                Marshal.Release(itemPointer);
            }
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(nint window, in Guid iid, out nint result);

        [PreserveSig]
        int CreateForMonitor(nint monitor, in Guid iid, out nint result);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int RoGetActivationFactory(string activatableClassId, ref Guid iid, out nint factory);

    private static T GetActivationFactory<T>(string activatableClassId) where T : class
    {
        var iid = typeof(T).GUID;
        var hr = RoGetActivationFactory(activatableClassId, ref iid, out var factory);

        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        try
        {
            return (T)Marshal.GetTypedObjectForIUnknown(factory, typeof(T));
        }
        finally
        {
            Marshal.Release(factory);
        }
    }
}
