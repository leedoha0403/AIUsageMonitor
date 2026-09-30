using System.Windows.Controls;
using AIUsage.Presentation.Views;
using Dora.Widget.Abstractions;

namespace AIUsage.Widget;

// Wraps the shared summary view (a XAML control cannot be subclassed across assemblies) and adds the Host hook
// that tells it which display mode to render. DataContext flows through to the inner view.
public sealed class HostSummaryView : ContentControl, IDisplayModeAware
{
    private readonly UsageSummaryView _inner = new();

    public HostSummaryView()
    {
        HostThemeBridge.ApplyFont(_inner);
        HostThemeBridge.Watch(this);
        Content = _inner;
        IsTabStop = false;
    }

    public UsageDisplayMode DisplayMode => _inner.DisplayMode;

    public void OnDisplayModeChanged(WidgetDisplayMode mode) => _inner.DisplayMode = Map(mode);

    public static UsageDisplayMode Map(WidgetDisplayMode mode) => mode switch
    {
        WidgetDisplayMode.Collapsed => UsageDisplayMode.Collapsed,
        WidgetDisplayMode.Compact => UsageDisplayMode.Compact,
        _ => UsageDisplayMode.Natural
    };
}
