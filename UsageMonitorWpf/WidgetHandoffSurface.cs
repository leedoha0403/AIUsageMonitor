using System.Windows.Threading;
using AIUsage.Presentation.Handoff;

namespace UsageMonitorWpf;

// Lets the hand-over service drive the mini widget window from pipe threads.
public sealed class WidgetHandoffSurface : IHandoffSurface
{
    private readonly Dispatcher _dispatcher;
    private readonly WidgetWindow _widget;

    public WidgetHandoffSurface(Dispatcher dispatcher, WidgetWindow widget)
    {
        _dispatcher = dispatcher;
        _widget = widget;
    }

    public Task RunOnUiAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public void ShowAt(double x, double y, double width, double height, double dpi) =>
        _widget.ShowAtScreenBounds(x, y, width, height, dpi);

    public void Hide() => _widget.HideWidget();
}
