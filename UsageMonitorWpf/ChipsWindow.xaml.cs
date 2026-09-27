using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using UsageMonitorWpf.ViewModels;

namespace UsageMonitorWpf;

// Always-visible chips docked just above the taskbar near the tray. Click toggles the flyout (mini widget),
// double-click opens the dashboard, drag moves it (position is remembered).
public partial class ChipsWindow : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSizeMoveActivate = 0x0001 | 0x0002 | 0x0010;
    private readonly MainViewModel _viewModel;
    private readonly Action _toggleFlyout;
    private readonly Action _openDashboard;
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public ChipsWindow(MainViewModel viewModel, Action toggleFlyout, Action openDashboard)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _toggleFlyout = toggleFlyout;
        _openDashboard = openDashboard;
        WindowOpacity.Attach(this, viewModel);
        SizeChanged += (_, _) => { if (!_viewModel.State.ChipsLeft.HasValue) PlaceDefault(); };
        // The taskbar raises itself over topmost windows when clicked; re-assert our z-order.
        _topmostTimer.Tick += (_, _) =>
        {
            if (IsVisible) SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSizeMoveActivate);
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) _topmostTimer.Start(); else _topmostTimer.Stop(); };
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
            _openDashboard();
            return;
        }

        var before = new System.Windows.Point(Left, Top);
        DragMove();
        if (Math.Abs(Left - before.X) < 2 && Math.Abs(Top - before.Y) < 2)
        {
            _toggleFlyout();
        }
        else
        {
            _viewModel.SaveChipsPlacement(Left, Top);
        }
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e) => _openDashboard();

    private void HideChips_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowTaskbarChips = false;
        Hide();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
