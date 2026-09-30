using System.Windows;
using System.Windows.Controls;
using AIUsage.Presentation.Controls;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Presentation.Views;

// Full detail surface (overview, history graphs, accounts, snapshot, diagnostics, scheduled refresh).
// It never assumes it owns a window: a host shell wraps it, and may append its own tabs (e.g. app settings).
public partial class UsageDetailView : System.Windows.Controls.UserControl
{
    public UsageDetailView()
    {
        InitializeComponent();
    }

    public void AddTab(TabItem tab) => DetailTabs.Items.Add(tab);

    // The feature's own options as a tab; used by hosts that do not bring an app-level settings tab.
    public void AddFeatureSettingsTab()
    {
        var tab = new TabItem { Content = new FeatureSettingsView() };
        tab.SetResourceReference(HeaderedContentControl.HeaderProperty, "ui.tab.settings");
        AddTab(tab);
    }

    private UsageFeatureViewModel? ViewModel => DataContext as UsageFeatureViewModel;

    private void LegendItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.HighlightedSeriesName = sender switch
        {
            FrameworkElement { DataContext: ChartSeries series } => series.Name,
            FrameworkElement { DataContext: ChartArea area } => area.Name,
            _ => null
        };
    }

    private void LegendItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel != null) ViewModel.HighlightedSeriesName = null;
    }
}
