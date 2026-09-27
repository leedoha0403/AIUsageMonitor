using System.ComponentModel;
using System.Windows;
using UsageMonitorWpf.ViewModels;

namespace UsageMonitorWpf;

// Applies the mini-mode opacity setting to a window; optionally turns fully opaque while hovered.
public static class WindowOpacity
{
    public static void Attach(Window window, MainViewModel viewModel)
    {
        void Apply()
        {
            window.Opacity = viewModel.HoverOpaque && window.IsMouseOver ? 1.0 : viewModel.WidgetOpacity;
        }

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.WidgetOpacity) or nameof(MainViewModel.HoverOpaque)) Apply();
        };
        viewModel.PropertyChanged += handler;
        window.MouseEnter += (_, _) => Apply();
        window.MouseLeave += (_, _) => Apply();
        window.Closed += (_, _) => viewModel.PropertyChanged -= handler;
        Apply();
    }
}
