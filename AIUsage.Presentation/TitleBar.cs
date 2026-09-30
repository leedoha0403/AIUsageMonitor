using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AIUsage.Presentation;

// Native title bar follows the theme (Windows 10 20H1+ / 11). The shell that owns the theme sets IsDark.
public static class TitleBar
{
    public static bool IsDark { get; set; }

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
