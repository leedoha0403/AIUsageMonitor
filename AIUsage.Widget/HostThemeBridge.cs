using Application = System.Windows.Application;

namespace AIUsage.Widget;

// Maps the Host's ModuleDock.* theme (WIDGET_DESIGN_GUIDE.md) onto the resource keys the shared presentation
// views already use. Outside a Host the ModuleDock.* keys do not exist, so nothing is overridden and the views
// keep their standalone defaults.
public static class HostThemeBridge
{
    private static readonly (string Mine, string Host)[] Map =
    {
        ("InkBrush", "ModuleDock.Brush.Text"),
        ("HeaderBrush", "ModuleDock.Brush.Text"), // the dashboard banner sits on a dark gradient inside a Host
        ("MutedBrush", "ModuleDock.Brush.Muted"),
        ("AccentBrush", "ModuleDock.Brush.Accent"),
        ("PanelBrush", "ModuleDock.Brush.Surface"),
        ("CanvasBrush", "ModuleDock.Brush.Background"),
        ("LineBrush", "ModuleDock.Brush.Border"),
        ("AppFont", "ModuleDock.Font.Family"),
    };

    public const string HostFontKey = "ModuleDock.Font.Family";

    // The Host swaps its ModuleDock.* values on every theme change and bumps this key. Resource references only
    // refresh for elements in a visual tree, so each view root watches it and re-copies the values.
    private static readonly System.Windows.DependencyProperty TokenProperty = System.Windows.DependencyProperty.RegisterAttached(
        "Token", typeof(object), typeof(HostThemeBridge), new System.Windows.PropertyMetadata(null, (_, _) => Apply()));

    public static void Watch(System.Windows.FrameworkElement view)
    {
        if (Application.Current?.TryFindResource(TokenKey) != null)
            view.SetResourceReference(TokenProperty, TokenKey);
    }

    private const string TokenKey = "ModuleDock.Theme.Token";

    // Copies Host values over the application-level keys (frozen brushes, shared as-is).
    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;
        foreach (var (mine, host) in Map)
        {
            if (app.TryFindResource(host) is { } value) app.Resources[mine] = value;
        }
    }

    // The presentation Styles.xaml defines its own AppFont (Spoqa) inside each view, which shadows the
    // application-level key; the guide forbids bundled fonts inside a Host, so point the view root at the Host font.
    public static void ApplyFont(System.Windows.Controls.Control view)
    {
        if (Application.Current?.TryFindResource(HostFontKey) != null)
            view.SetResourceReference(System.Windows.Controls.Control.FontFamilyProperty, HostFontKey);
    }
}
