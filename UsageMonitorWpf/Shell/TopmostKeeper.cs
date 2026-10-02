using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UsageMonitorWpf.Shell;

// The taskbar (and other topmost windows) can raise themselves over a topmost window when clicked; this puts
// it back on top without moving, resizing or activating it.
public static class TopmostKeeper
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSizeMoveActivate = 0x0001 | 0x0002 | 0x0010;

    public static void Reassert(Window window)
    {
        if (!window.IsVisible) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoSizeMoveActivate);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
