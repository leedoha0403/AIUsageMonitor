using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using UsageMonitorWpf.Core;
using UsageMonitorWpf.Storage;
using UsageMonitorWpf.ViewModels;
using Forms = System.Windows.Forms;

namespace UsageMonitorWpf;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly WidgetWindow _widgetWindow;
    private readonly ChipsWindow _chipsWindow;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new StateStore());
        DataContext = _viewModel;
        _widgetWindow = new WidgetWindow(_viewModel, ShowDashboard);
        _chipsWindow = new ChipsWindow(_viewModel, ToggleFlyout, ShowDashboard);
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.NotificationRequested += ShowNotification;

        if (_viewModel.State.WindowLeft.HasValue && _viewModel.State.WindowTop.HasValue)
        {
            Left = _viewModel.State.WindowLeft.Value;
            Top = _viewModel.State.WindowTop.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ShowInTaskbar = true;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(Forms.SystemInformation.SmallIconSize),
            Visible = true,
            Text = "Usage Monitor"
        };

        BuildTrayMenu();
        _viewModel.LanguageChanged += BuildTrayMenu;
        ThemeService.Changed += BuildTrayMenu;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
        // Windows 11 can report a balloon "click" when one balloon replaces another or times out,
        // so ignore clicks that arrive right after showing and keep the actions harmless (UI only).
        _notifyIcon.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            if (DateTime.Now - _balloonShownAt > TimeSpan.FromSeconds(1.5)) action?.Invoke();
        });
        _viewModel.LoginPromptRequested += ShowLoginPrompt;
        _notifyIcon.BalloonTipClosed += (_, _) => _balloonAction = null;

        Closing += (_, e) =>
        {
            if (_isExiting) return;
            // Closing the dashboard (X button) should only hide it to the tray, not exit the app.
            e.Cancel = true;
            _viewModel.SaveWindowPlacement(Left, Top);
            Hide();
            ShowInTaskbar = false;
        };
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
            _viewModel.NotificationRequested -= ShowNotification;
            _viewModel.LanguageChanged -= BuildTrayMenu;
            ThemeService.Changed -= BuildTrayMenu;
            _viewModel.LoginPromptRequested -= ShowLoginPrompt;
            _viewModel.SaveWindowPlacement(Left, Top);
            _widgetWindow.Close();
            _chipsWindow.Close();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        };
        _signInLaunchHandled = !App.StartedAtSignIn;
        ApplyWindowVersion();
        _signInLaunchHandled = true;
        ApplyChips();
        try
        {
            StartupService.RepairIfStale();
        }
        catch
        {
        }
        _notifyIcon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleFlyout); };
        Dispatcher.BeginInvoke(() => _ = _viewModel.RefreshAsync(), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static System.Drawing.Icon LoadAppIcon(System.Drawing.Size size)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        if (resource == null) return System.Drawing.SystemIcons.Application;
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, size);
    }

    private void BuildTrayMenu()
    {
        var old = _notifyIcon.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("ui.openDashboard"), null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add(Loc.T("ui.openWidget"), null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add(Loc.T("ui.refresh"), null, (_, _) => Dispatcher.Invoke(() => _ = _viewModel.RefreshAsync(force: true)));
        menu.Items.Add(Loc.T("ui.saveSnapshot"), null, (_, _) => Dispatcher.Invoke(_viewModel.SaveManualSnapshot));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.widgetMode"), _viewModel.WidgetModes, () => _viewModel.WidgetMode, value => _viewModel.WidgetMode = value));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.windowVersion"), _viewModel.WindowVersions, () => _viewModel.WindowVersion, value => _viewModel.WindowVersion = value));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.collectionLevel"), _viewModel.CollectionLevels, () => _viewModel.CollectionLevel, value => _viewModel.CollectionLevel = value));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.taskbarChips"), _viewModel.OnOffOptions, () => _viewModel.ShowTaskbarChips ? "On" : "Off", value => _viewModel.ShowTaskbarChips = value == "On"));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.displayUsageAs"), _viewModel.DisplayOptions, () => _viewModel.DisplayUsageAs, value => _viewModel.DisplayUsageAs = value));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.runAtStartup"), _viewModel.OnOffOptions, () => _viewModel.RunAtStartup ? "On" : "Off", value => _viewModel.RunAtStartup = value == "On"));
        menu.Items.Add(CreateChoiceMenu(Loc.T("ui.language"), _viewModel.Languages, () => _viewModel.Language, value => _viewModel.Language = value));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("ui.exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        menu.Renderer = new ThemedMenuRenderer(ThemeService.IsDark);
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Text = Loc.T("ui.appName");
        old?.Dispose();
    }

    private Action? _balloonAction;
    private DateTime _balloonShownAt;

    private void ShowLoginPrompt()
    {
        _widgetWindow.ShowAtSavedPlacement();
        _widgetWindow.Reveal();
    }

    private void ShowNotification(string title, string message, Action? onClick)
    {
        _balloonAction = onClick;
        _balloonShownAt = DateTime.Now;
        // ToolTipIcon.None: Windows shows the tray icon (the app icon) in the notification.
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.None;
        _notifyIcon.ShowBalloonTip(8000, title, message, Forms.ToolTipIcon.None);
    }

    private void ApplyChips()
    {
        if (_viewModel.ShowTaskbarChips) _chipsWindow.ShowChips();
        else _chipsWindow.Hide();
    }

    // Chips/tray click: the widget acts as a flyout just above the chips.
    private void ToggleFlyout()
    {
        if (_widgetWindow.IsVisible)
        {
            _widgetWindow.HideWidget();
            return;
        }

        if (_chipsWindow.IsVisible)
        {
            _widgetWindow.ShowAbove(new Rect(_chipsWindow.Left, _chipsWindow.Top, _chipsWindow.ActualWidth, _chipsWindow.ActualHeight));
        }
        else
        {
            _widgetWindow.ShowAtSavedPlacement();
        }
    }

    private Forms.ToolStripMenuItem CreateChoiceMenu(string title, IEnumerable<OptionItem> options, Func<string> current, Action<string> select)
    {
        var parent = new Forms.ToolStripMenuItem(title);
        foreach (var option in options)
        {
            var value = (string)option.Value;
            var item = new Forms.ToolStripMenuItem(option.Label) { Tag = value };
            item.Click += (_, _) => Dispatcher.Invoke(() => select(value));
            parent.DropDownItems.Add(item);
        }
        parent.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripMenuItem item in parent.DropDownItems)
            {
                item.Checked = (string)item.Tag! == current();
            }
        };
        return parent;
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private bool _isExiting;

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void ShowWidget()
    {
        _widgetWindow.ShowAtSavedPlacement();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsMiniVersion) || e.PropertyName == nameof(MainViewModel.WindowVersion))
        {
            ApplyWindowVersion();
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowTaskbarChips))
        {
            ApplyChips();
        }
    }

    private bool _signInLaunchHandled;

    private void ApplyWindowVersion()
    {
        if (_viewModel.IsMiniVersion)
        {
            Hide();
            ShowWidget();
        }
        else if (App.StartedAtSignIn && !_signInLaunchHandled)
        {
            // Sign-in launch in Expanded mode: stay in the tray/chips; the dashboard opens on demand.
            _signInLaunchHandled = true;
            Hide();
        }
        else
        {
            _widgetWindow.HideWidget();
            ShowDashboard();
            Width = Math.Max(Width, 960);
            Height = Math.Max(Height, 700);
        }
    }
}
