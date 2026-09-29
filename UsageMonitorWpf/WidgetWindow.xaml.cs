using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UsageMonitorWpf.Core;
using UsageMonitorWpf.ViewModels;
using Forms = System.Windows.Forms;

namespace UsageMonitorWpf;

public enum DockEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom
}

// Mini widget with magnetic edge docking. Dragged near a screen edge it snaps to it; a docked widget can be
// folded so it slides out of sight into that edge, leaving an auto-hide handle. Hovering the handle slides the
// widget back out, and it slides away again shortly after the mouse leaves.
public partial class WidgetWindow : Window
{
    private const double SnapDistance = 24;
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(220));

    private readonly Action _openDashboard;
    private readonly Action _exitApp;
    private readonly Action _showChips;
    private readonly Func<bool> _isChipsShown;
    private readonly MainViewModel _viewModel;
    private readonly HandleWindow _handle = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DockEdge _edge;
    private Rect _dockArea = Rect.Empty;
    private bool _folded;
    private bool _revealed;
    private bool _dragging;
    private bool _moved;
    private bool _animating;
    private System.Windows.Point _dragStartCursor;
    private System.Windows.Point _dragStartWindow;

    public WidgetWindow(MainViewModel viewModel, Action openDashboard, Action exitApp, Action showChips, Func<bool> isChipsShown)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _openDashboard = openDashboard;
        _exitApp = exitApp;
        _showChips = showChips;
        _isChipsShown = isChipsShown;
        WindowOpacity.Attach(this, viewModel);
        _edge = Enum.TryParse<DockEdge>(viewModel.State.WidgetDockEdge, out var edge) ? edge : DockEdge.None;
        _folded = _edge != DockEdge.None && viewModel.State.WidgetFolded;

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_folded && _revealed && !_dragging && !CursorInside() && !ContextMenuOpen()) SlideIn();
        };
        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (_folded && _revealed) _hideTimer.Start();
        };
        _handle.MouseEnter += (_, _) => Reveal();
        // Keep the widget flush with its edge when its height changes (mode switch, accounts added).
        SizeChanged += (_, _) =>
        {
            if (_edge == DockEdge.None || _dragging || _animating || !IsVisible) return;
            var target = ShownPosition();
            Left = target.X;
            Top = target.Y;
        };
        // Alt+F4 or an OS close request should only hide the widget (to the tray), like the dashboard's X button.
        // Only CloseForExit (app shutdown) performs a real close.
        Closing += (_, e) =>
        {
            if (_closingForExit) return;
            e.Cancel = true;
            HideWidget();
        };
        Closed += (_, _) => _handle.CloseForExit();
        _viewModel.LanguageChanged += UpdateFoldButton;
        UpdateFoldButton();
    }

    private bool _closingForExit;

    // Called only when the whole app is shutting down; lets this window actually close.
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    // Shows the widget at its saved spot, re-aligned to its docked edge; a folded widget shows only its handle.
    public void ShowAtSavedPlacement()
    {
        var state = _viewModel.State;
        StopAnimation();
        if (!IsVisible) Show();
        UpdateLayout();
        if (state.WidgetLeft.HasValue && state.WidgetTop.HasValue && IsOnScreen(state.WidgetLeft.Value, state.WidgetTop.Value))
        {
            Left = state.WidgetLeft.Value;
            Top = state.WidgetTop.Value;
        }
        else
        {
            var area = WorkArea();
            Left = area.Right - ActualWidth - 22;
            Top = area.Bottom - ActualHeight - 60;
            _edge = DockEdge.None;
            _folded = false;
        }

        if (_edge != DockEdge.None)
        {
            _dockArea = WorkArea();
            var shown = ShownPosition();
            Left = shown.X;
            Top = shown.Y;
            if (_folded)
            {
                _revealed = false;
                Hide();
                ShowHandle();
                UpdateFoldButton();
                return;
            }
        }
        _revealed = true;
        _handle.Hide();
        UpdateFoldButton();
        Activate();
    }

    // Brings a folded widget out (handle hover, login prompt, chips click).
    public void Reveal()
    {
        if (_folded && !_revealed) SlideOut();
    }

    // Flyout opened from the chips: an undocked widget pops up just above them.
    public void ShowAbove(Rect anchor)
    {
        if (_edge != DockEdge.None)
        {
            ShowAtSavedPlacement();
            Reveal();
            return;
        }
        Show();
        UpdateLayout();
        var area = WorkArea();
        Left = Math.Clamp(anchor.Right - ActualWidth, area.Left, area.Right - ActualWidth);
        Top = Math.Max(area.Top, anchor.Top - ActualHeight - 8);
        Activate();
    }

    // Hides the widget and its handle entirely (× button, chips toggle, switching to the dashboard).
    public void HideWidget()
    {
        _hideTimer.Stop();
        StopAnimation();
        _handle.Hide();
        Hide();
    }

    // True while the widget is on screen or folded behind its handle.
    public bool IsShown => IsVisible || _handle.IsVisible;

    // ---- Dragging with magnetic snap

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _openDashboard();
            return;
        }

        StopAnimation();
        _hideTimer.Stop();
        _dragging = true;
        _moved = false;
        _dragStartCursor = CursorDip();
        _dragStartWindow = new System.Windows.Point(Left, Top);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void Widget_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging) DragTo(CursorDip());
    }

    private void DragTo(System.Windows.Point cursor)
    {
        var left = _dragStartWindow.X + cursor.X - _dragStartCursor.X;
        var top = _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y;
        if (!_moved && Math.Abs(left - _dragStartWindow.X) + Math.Abs(top - _dragStartWindow.Y) < 3) return;
        _moved = true;

        var area = WorkArea(new System.Windows.Point(left + ActualWidth / 2, top + ActualHeight / 2));
        var edge = DockEdge.None;
        var best = SnapDistance;

        void Try(double distance, DockEdge candidate)
        {
            if (Math.Abs(distance) < best)
            {
                best = Math.Abs(distance);
                edge = candidate;
            }
        }

        Try(left - area.Left, DockEdge.Left);
        Try(area.Right - (left + ActualWidth), DockEdge.Right);
        Try(top - area.Top, DockEdge.Top);
        Try(area.Bottom - (top + ActualHeight), DockEdge.Bottom);

        // Magnetic: snap every edge within reach (corners snap both ways); the closest edge becomes the dock edge.
        if (Math.Abs(left - area.Left) < SnapDistance) left = area.Left;
        else if (Math.Abs(area.Right - (left + ActualWidth)) < SnapDistance) left = area.Right - ActualWidth;
        if (Math.Abs(top - area.Top) < SnapDistance) top = area.Top;
        else if (Math.Abs(area.Bottom - (top + ActualHeight)) < SnapDistance) top = area.Bottom - ActualHeight;

        Left = left;
        Top = top;
        _dockArea = area;
        if (edge != _edge)
        {
            _edge = edge;
            if (_edge == DockEdge.None) _folded = false;
            UpdateFoldButton();
        }
    }

    private void Widget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        // The last move before release is not always delivered; apply the final cursor position (and snap) here.
        DragTo(CursorDip());
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        if (!_moved) return;
        if (_folded) _revealed = true;
        SavePlacement();
        if (_folded && !CursorInside()) _hideTimer.Start();
    }

    // ---- Folding

    private void Fold_Click(object sender, RoutedEventArgs e)
    {
        if (_edge == DockEdge.None) return;
        _folded = !_folded;
        _dockArea = WorkArea();
        if (_folded) SlideIn();
        SavePlacement();
        UpdateFoldButton();
    }

    private void SlideIn()
    {
        _revealed = false;
        var shown = ShownPosition();
        Animate(HiddenPosition(), () =>
        {
            Hide();
            Left = shown.X;
            Top = shown.Y;
            ShowHandle();
        });
    }

    private void SlideOut()
    {
        _handle.Hide();
        var shown = ShownPosition();
        var hidden = HiddenPosition();
        if (!IsVisible)
        {
            Left = hidden.X;
            Top = hidden.Y;
            Show();
        }
        _revealed = true;
        Animate(shown, () =>
        {
            // The mouse may already have moved on before the widget arrived.
            if (!CursorInside()) _hideTimer.Start();
        });
    }

    private void ShowHandle()
    {
        var shown = ShownPosition();
        _handle.ShowAt(_edge, DockArea(), new Rect(shown.X, shown.Y, ActualWidth, ActualHeight));
    }

    private Rect DockArea() => _dockArea.IsEmpty ? WorkArea() : _dockArea;

    private System.Windows.Point ShownPosition()
    {
        var area = DockArea();
        var left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        var top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        return _edge switch
        {
            DockEdge.Left => new(area.Left, top),
            DockEdge.Right => new(area.Right - ActualWidth, top),
            DockEdge.Top => new(left, area.Top),
            DockEdge.Bottom => new(left, area.Bottom - ActualHeight),
            _ => new(Left, Top)
        };
    }

    private System.Windows.Point HiddenPosition()
    {
        var shown = ShownPosition();
        var area = DockArea();
        return _edge switch
        {
            DockEdge.Left => new(area.Left - ActualWidth, shown.Y),
            DockEdge.Right => new(area.Right, shown.Y),
            DockEdge.Top => new(shown.X, area.Top - ActualHeight),
            DockEdge.Bottom => new(shown.X, area.Bottom),
            _ => shown
        };
    }

    private void Animate(System.Windows.Point target, Action? completed = null)
    {
        StopAnimation();
        _animating = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var x = new DoubleAnimation(Left, target.X, SlideDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var y = new DoubleAnimation(Top, target.Y, SlideDuration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        x.Completed += (_, _) =>
        {
            if (!_animating) return;
            // Hand the final value back to the property so later drags start from the right place.
            Left = target.X;
            Top = target.Y;
            _animating = false;
            completed?.Invoke();
        };
        BeginAnimation(LeftProperty, x);
        BeginAnimation(TopProperty, y);
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        var left = Left;
        var top = Top;
        _animating = false;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = left;
        Top = top;
    }

    private void UpdateFoldButton()
    {
        FoldButton.Visibility = _edge == DockEdge.None ? Visibility.Collapsed : Visibility.Visible;
        // Arrow points toward the edge to fold, away from it to unfold.
        var toward = _edge switch
        {
            DockEdge.Left => "◀",
            DockEdge.Right => "▶",
            DockEdge.Top => "▲",
            DockEdge.Bottom => "▼",
            _ => ""
        };
        var away = _edge switch
        {
            DockEdge.Left => "▶",
            DockEdge.Right => "◀",
            DockEdge.Top => "▼",
            DockEdge.Bottom => "▲",
            _ => ""
        };
        FoldButton.Content = _folded ? away : toward;
        FoldButton.ToolTip = _folded ? Loc.T("ui.unfold") : Loc.T("ui.fold");
        if (_edge == DockEdge.None) _handle.Hide();
    }

    private void SavePlacement()
    {
        var shown = _edge == DockEdge.None ? new System.Windows.Point(Left, Top) : ShownPosition();
        _viewModel.SaveWidgetPlacement(shown.X, shown.Y, _edge.ToString(), _folded);
    }

    private bool ContextMenuOpen() => (Content as FrameworkElement)?.ContextMenu?.IsOpen == true;

    private bool CursorInside()
    {
        if (!IsVisible) return false;
        var cursor = CursorDip();
        return new Rect(Left, Top, ActualWidth, ActualHeight).Contains(cursor);
    }

    // ---- Screen geometry (DIPs, per monitor)

    private Rect WorkArea() => WorkArea(new System.Windows.Point(Left + ActualWidth / 2, Top + ActualHeight / 2));

    private Rect WorkArea(System.Windows.Point dipPoint)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return SystemParameters.WorkArea;
        var toDevice = source.CompositionTarget.TransformToDevice;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var device = toDevice.Transform(dipPoint);
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)device.X, (int)device.Y));
        var wa = screen.WorkingArea;
        return new Rect(fromDevice.Transform(new System.Windows.Point(wa.Left, wa.Top)), fromDevice.Transform(new System.Windows.Point(wa.Right, wa.Bottom)));
    }

    private System.Windows.Point CursorDip()
    {
        GetCursorPos(out var p);
        var source = PresentationSource.FromVisual(this) ?? PresentationSource.FromVisual(_handle);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return fromDevice.Transform(new System.Windows.Point(p.X, p.Y));
    }

    private static bool IsOnScreen(double left, double top) =>
        left + 40 >= SystemParameters.VirtualScreenLeft && top + 10 >= SystemParameters.VirtualScreenTop &&
        left + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        top + 10 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        _openDashboard();
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        HideWidget();
    }

    private void ShowChips_Click(object sender, RoutedEventArgs e)
    {
        _showChips();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        ShowChipsMenuItem.Visibility = _isChipsShown() ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _exitApp();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
}
