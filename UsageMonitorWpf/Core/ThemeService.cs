using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UsageMonitorWpf.Core;

public static class ThemeService
{
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    private static readonly CustomThemeSettings DefaultCustomTheme = new();

    public static void Apply(string theme, CustomThemeSettings? customTheme = null)
    {
        var effectiveTheme = theme;
        if (theme == "System")
        {
            effectiveTheme = IsSystemDark() ? "Dark" : "Light";
        }

        IsDark = effectiveTheme == "Dark";
        if (effectiveTheme == "Custom")
        {
            var custom = customTheme ?? DefaultCustomTheme;
            Set(custom.Ink, custom.Muted, custom.Accent, custom.Panel, custom.Canvas, custom.Line, custom.Header,
                custom.HeaderStart, custom.HeaderMiddle, custom.HeaderEnd);
        }
        else if (IsDark)
        {
            Set("#F4F7F5", "#A9B3BD", "#3FDDB2", "#171B1D", "#101315", "#2A3336", "#F4F7F5",
                "#0B1418", "#0F2A2B", "#17806B");
        }
        else
        {
            Set("#15191D", "#66717C", "#17967A", "#FFFFFF", "#F6F8F7", "#2415191D", "#FFFFFF",
                "#0B1418", "#0F2A2B", "#17806B");
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

    private static void Set(
        string ink,
        string muted,
        string accent,
        string panel,
        string canvas,
        string line,
        string header,
        string headerStart,
        string headerMiddle,
        string headerEnd)
    {
        var resources = System.Windows.Application.Current.Resources;
        resources["InkBrush"] = Brush(ink, "#15191D");
        resources["MutedBrush"] = Brush(muted, "#66717C");
        resources["AccentBrush"] = Brush(accent, "#17967A");
        resources["PanelBrush"] = Brush(panel, "#FFFFFF");
        resources["CanvasBrush"] = Brush(canvas, "#F6F8F7");
        resources["LineBrush"] = Brush(line, "#2415191D");
        resources["HeaderBrush"] = Brush(header, "#15191D");
        resources["HeaderStartColor"] = Color(headerStart, "#0B1418");
        resources["HeaderMiddleColor"] = Color(headerMiddle, "#0F2A2B");
        resources["HeaderEndColor"] = Color(headerEnd, "#17806B");
    }

    private static SolidColorBrush Brush(string hex, string fallback)
    {
        var brush = new SolidColorBrush(Color(hex, fallback));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Color Color(string hex, string fallback)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback);
        }
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
