using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AIUsage.Core;

namespace UsageMonitorWpf;

// Auto-hide handle shown on the screen edge while the mini widget is folded away.
// It pulses when it appears and grows under the mouse so it is easy to find; hovering it brings the widget back.
public sealed class HandleWindow : Window
{
    private const double Thickness = 16;
    private const double Length = 92;
    private readonly Border _tab;
    private readonly TextBlock _arrow;
    private readonly TextBlock _grip;
    private readonly StackPanel _content;

    public HandleWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Title = "Usage Widget Handle";

        _arrow = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _grip = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _content = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _content.Children.Add(_arrow);
        _content.Children.Add(_grip);
        _tab = new Border { Child = _content, Cursor = System.Windows.Input.Cursors.Hand };
        _tab.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _tab.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.35 };
        Content = _tab;

        MouseEnter += (_, _) => { BeginAnimation(OpacityProperty, null); Opacity = 1; };
        MouseLeave += (_, _) => Opacity = 0.9;

        // A click can still activate this window despite ShowActivated=false, so Alt+F4 (or any close
        // request) could otherwise really close it — and once a WPF Window is closed it can never Show()
        // again, permanently breaking the folded widget's hover-to-reveal handle for the rest of the session.
        Closing += (_, e) =>
        {
            if (_closingForExit) return;
            e.Cancel = true;
            Hide();
        };
    }

    private bool _closingForExit;

    // Called only when the whole app is shutting down; lets this window actually close.
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    public void ShowAt(DockEdge edge, Rect area, Rect widget)
    {
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        Width = vertical ? Thickness : Length;
        Height = vertical ? Length : Thickness;
        _content.Orientation = vertical ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;
        // Arrow points out of the edge: "hover here and it comes out this way".
        _arrow.Text = edge switch { DockEdge.Left => "›", DockEdge.Right => "‹", DockEdge.Top => "⌄", _ => "⌃" };
        _grip.Text = vertical ? "⋮" : "⋯";
        _grip.Margin = vertical ? new Thickness(0, 2, 0, 0) : new Thickness(4, 0, 0, 0);
        _tab.CornerRadius = edge switch
        {
            DockEdge.Left => new CornerRadius(0, 8, 8, 0),
            DockEdge.Right => new CornerRadius(8, 0, 0, 8),
            DockEdge.Top => new CornerRadius(0, 0, 8, 8),
            _ => new CornerRadius(8, 8, 0, 0)
        };
        ToolTip = Loc.T("ui.handleTip");

        switch (edge)
        {
            case DockEdge.Left:
                Left = area.Left;
                Top = Math.Clamp(widget.Top + widget.Height / 2 - Height / 2, area.Top, area.Bottom - Height);
                break;
            case DockEdge.Right:
                Left = area.Right - Width;
                Top = Math.Clamp(widget.Top + widget.Height / 2 - Height / 2, area.Top, area.Bottom - Height);
                break;
            case DockEdge.Top:
                Top = area.Top;
                Left = Math.Clamp(widget.Left + widget.Width / 2 - Width / 2, area.Left, area.Right - Width);
                break;
            default:
                Top = area.Bottom - Height;
                Left = Math.Clamp(widget.Left + widget.Width / 2 - Width / 2, area.Left, area.Right - Width);
                break;
        }

        var wasVisible = IsVisible;
        Show();
        if (!wasVisible) Pulse();
    }

    public void ReassertTopmost() => Shell.TopmostKeeper.Reassert(this);

    // A few soft blinks right after folding so the eye finds the handle.
    private void Pulse()
    {
        var pulse = new DoubleAnimation(1.0, 0.35, new Duration(TimeSpan.FromMilliseconds(420)))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        pulse.Completed += (_, _) => Opacity = 0.9;
        BeginAnimation(OpacityProperty, pulse);
    }
}
