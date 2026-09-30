using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Presentation.Views;

// What the owner of the chips (the standalone app or a Host widget) does for the chips' gestures. Optional
// entries are left out of the menu.
public sealed class ChipsActions
{
    public required Action Click { get; init; }
    public required Action OpenDashboard { get; init; }
    public Action? ShowWidget { get; init; }
    public Func<bool>? IsWidgetShown { get; init; }
    public Action? Exit { get; init; }
    public Func<bool>? HoverOpaque { get; init; }
}

// Always-visible chips docked just above the taskbar near the tray. Click runs Click, double-click opens the
// dashboard, drag moves it (position is remembered in the feature state).
public partial class UsageChipsWindow : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSizeMoveActivate = 0x0001 | 0x0002 | 0x0010;
    private readonly UsageFeatureViewModel _viewModel;
    private readonly ChipsActions _actions;
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _closingForExit;

    public UsageChipsWindow(UsageFeatureViewModel viewModel, ChipsActions actions)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _actions = actions;
        ShowWidgetMenuItem.Visibility = actions.ShowWidget == null ? Visibility.Collapsed : Visibility.Visible;
        ExitMenuItem.Visibility = actions.Exit == null ? Visibility.Collapsed : Visibility.Visible;

        System.ComponentModel.PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(UsageFeatureViewModel.ChipsOpacity)) ApplyOpacity();
        };
        viewModel.PropertyChanged += onChanged;
        MouseEnter += (_, _) => ApplyOpacity();
        MouseLeave += (_, _) => ApplyOpacity();
        Closed += (_, _) => viewModel.PropertyChanged -= onChanged;
        ApplyOpacity();

        SizeChanged += (_, _) => { if (!_viewModel.State.ChipsLeft.HasValue) PlaceDefault(); };
        // The taskbar raises itself over topmost windows when clicked; re-assert our z-order.
        _topmostTimer.Tick += (_, _) =>
        {
            if (IsVisible) SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSizeMoveActivate);
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) _topmostTimer.Start(); else _topmostTimer.Stop(); };
        // Alt+F4 or an OS close request only hides the chips; CloseForExit performs a real close.
        Closing += (_, e) =>
        {
            if (_closingForExit) return;
            e.Cancel = true;
            _viewModel.ShowTaskbarChips = false;
            Hide();
        };
    }

    // Opaque while hovered when the owner asks for it, otherwise the configured chips opacity.
    public void ApplyOpacity() =>
        Opacity = _actions.HoverOpaque?.Invoke() == true && IsMouseOver ? 1.0 : _viewModel.ChipsOpacity;

    // Called when the owner goes away (app shutdown, widget removed); lets this window actually close.
    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    public void ShowChips()
    {
        var state = _viewModel.State;
        if (state.ChipsLeft.HasValue && state.ChipsTop.HasValue && IsOnScreen(state.ChipsLeft.Value, state.ChipsTop.Value))
        {
            Left = state.ChipsLeft.Value;
            Top = state.ChipsTop.Value;
        }
        else
        {
            PlaceDefault();
        }
        Show();
    }

    private void PlaceDefault()
    {
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : 200;
        var height = ActualHeight > 0 ? ActualHeight : 28;
        Left = area.Right - width - 12;
        Top = area.Bottom - height - 6;
    }

    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
        left + 40 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        top + 10 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    private void Chips_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _actions.OpenDashboard();
            return;
        }

        var before = new System.Windows.Point(Left, Top);
        DragMove();
        if (Math.Abs(Left - before.X) < 2 && Math.Abs(Top - before.Y) < 2)
        {
            _actions.Click();
        }
        else
        {
            _viewModel.SaveChipsPlacement(Left, Top);
        }
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e) => _actions.OpenDashboard();

    private void HideChips_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowTaskbarChips = false;
        Hide();
    }

    private void ShowWidget_Click(object sender, RoutedEventArgs e) => _actions.ShowWidget?.Invoke();

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        ShowWidgetMenuItem.Visibility = _actions.ShowWidget == null || _actions.IsWidgetShown?.Invoke() == true
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => _actions.Exit?.Invoke();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
