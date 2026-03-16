using System.Runtime.InteropServices;

namespace ScreenFast.App.Interop;

internal static class NativeMethods
{
    internal const int GwlpWndProc = -4;
    internal const uint WmSize = 0x0005;
    internal const uint WmClose = 0x0010;
    internal const uint WmHotKey = 0x0312;
    internal const int SizeMinimized = 1;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const int SwHide = 0;
    internal const int SwRestore = 9;

    internal delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int newLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern nint CallWindowProc(nint previousWindowProcedure, nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    internal static nint SetWindowLongPtr(nint hWnd, int nIndex, nint newLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, newLong)
            : new(SetWindowLong32(hWnd, nIndex, newLong));
    }

    internal static nint GetWindowLongPtr(nint hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new(GetWindowLong32(hWnd, nIndex));
    }
}
