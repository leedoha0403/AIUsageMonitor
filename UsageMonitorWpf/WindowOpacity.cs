using UsageMonitorWpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using AIUsage.Presentation.ViewModels;

namespace UsageMonitorWpf;

// Applies a mini-mode opacity setting to a window; optionally turns fully opaque while hovered.
public static class WindowOpacity
{
    public static void Attach(Window window, MainViewModel viewModel) =>
        Attach(window, viewModel, () => viewModel.WidgetOpacity, nameof(MainViewModel.WidgetOpacity));

    private static void Attach(Window window, MainViewModel viewModel, Func<double> getOpacity, string opacityProperty)
    {
        void Apply()
        {
            window.Opacity = viewModel.HoverOpaque && window.IsMouseOver ? 1.0 : getOpacity();
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == opacityProperty || e.PropertyName == nameof(MainViewModel.HoverOpaque)) Apply();
        };
        viewModel.PropertyChanged += handler;
        window.MouseEnter += (_, _) => Apply();
        window.MouseLeave += (_, _) => Apply();
        window.Closed += (_, _) => viewModel.PropertyChanged -= handler;
        Apply();
    }
}
