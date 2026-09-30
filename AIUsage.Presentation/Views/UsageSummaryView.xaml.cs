using System.Windows;

namespace AIUsage.Presentation.Views;

// Summary surface used docked, floating and in the standalone mini widget. One shared view model; the
// display mode only changes which template is visible.
public partial class UsageSummaryView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(UsageDisplayMode), typeof(UsageSummaryView),
        new PropertyMetadata(UsageDisplayMode.Natural, (d, _) => ((UsageSummaryView)d).ApplyMode()));

    public UsageSummaryView()
    {
        InitializeComponent();
        ApplyMode();
    }

    public UsageDisplayMode DisplayMode
    {
        get => (UsageDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    private void ApplyMode()
    {
        if (NaturalPanel == null) return;
        NaturalPanel.Visibility = DisplayMode == UsageDisplayMode.Natural ? Visibility.Visible : Visibility.Collapsed;
        CompactPanel.Visibility = DisplayMode == UsageDisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        CollapsedPanel.Visibility = DisplayMode == UsageDisplayMode.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        NoticeHost.Visibility = DisplayMode == UsageDisplayMode.Collapsed ? Visibility.Collapsed : Visibility.Visible;
    }
}
