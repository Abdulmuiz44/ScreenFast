using System.Runtime.InteropServices;

namespace ScreenFast.Capture.Interop;

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(in Guid iid);
}

internal static class Direct3DSurfaceInterop
{
    public static nint GetInterfacePointer(object surface, in Guid iid)
    {
        var access = global::WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(surface);
        return access.GetInterface(iid);
    }
}
