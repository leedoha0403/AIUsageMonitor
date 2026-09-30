using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UsageMonitorWpf.Shell;

// Read-only lookup of a Host's main window rectangle (virtual-screen physical pixels), from the process id the
// Host announced in its hello. Nothing is reparented or modified; it only tells the widget where dropping docks it.
public static class HostWindowLocator
{
    public static bool TryGetRect(int hostPid, out double left, out double top, out double right, out double bottom)
    {
        left = top = right = bottom = 0;
        if (hostPid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(hostPid);
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle) || !GetWindowRect(handle, out var r)) return false;
            left = r.Left;
            top = r.Top;
            right = r.Right;
            bottom = r.Bottom;
            return right > left && bottom > top;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);
}
