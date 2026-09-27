using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UsageMonitorWpf.Core;

public static class ThemeService
{
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Apply(string theme)
    {
        var effectiveTheme = theme;
        if (theme == "System")
        {
            effectiveTheme = IsSystemDark() ? "Dark" : "Light";
        }

        IsDark = effectiveTheme == "Dark";
        if (IsDark)
        {
            Set("#F4F7F5", "#A9B3BD", "#3FDDB2", "#171B1D", "#101315", "#2A3336", "#0F1214");
        }
        else
        {
            Set("#15191D", "#66717C", "#17967A", "#FFFFFF", "#F6F8F7", "#2415191D", "#15191D");
        }

        foreach (Window window in System.Windows.Application.Current.Windows) ApplyTitleBar(window);
        Changed?.Invoke();
    }

    // Native title bar follows the theme (Windows 10 20H1+ / 11).
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void Set(string ink, string muted, string accent, string panel, string canvas, string line, string header)
    {
        var resources = System.Windows.Application.Current.Resources;
        resources["InkBrush"] = Brush(ink);
        resources["MutedBrush"] = Brush(muted);
        resources["AccentBrush"] = Brush(accent);
        resources["PanelBrush"] = Brush(panel);
        resources["CanvasBrush"] = Brush(canvas);
        resources["LineBrush"] = Brush(line);
        resources["HeaderBrush"] = Brush(header);
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch
        {
            return false;
        }
    }
}
