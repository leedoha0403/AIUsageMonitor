using System.Windows.Threading;
using AIUsage.Core;
using AIUsage.Core.Storage;
using AIUsage.Presentation;
using AIUsage.Presentation.ViewModels;
using AIUsage.Presentation.Views;
using Dora.Widget.Abstractions;

namespace AIUsage.Widget;

// Host adapter for the usage feature. Every surface (docked summary, floating summary, detail window) binds the
// same feature view model, so a change in one shows in all. Docking, dragging, focus and windows are the Host's.
public sealed class AIUsageWidget : IComposableWidget
{
    private static readonly TimeSpan OwnershipPoll = TimeSpan.FromSeconds(5);

    private IWidgetContext? _context;
    private FeatureStateSnapshot? _restored;
    private HostStateStore? _store;
    private UsageFeatureViewModel? _viewModel;
    private CollectionOwnership? _ownership;
    private DispatcherTimer? _ownershipTimer;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly string _standaloneMutexName;
    private readonly string _widgetMutexName;
    private readonly string _presenceMutexName;
    private Mutex? _presence;
    private Dispatcher? _dispatcher;
    private readonly bool _showChips;
    private UsageChipsWindow? _chips;

    // The Host's plugin loader only registers widgets with a truly parameterless constructor (optional
    // parameters do not count), so this one must stay.
    public AIUsageWidget() : this(null, null)
    {
    }

    // Mutex names are overridable so tests (and side-by-side installs) do not clash with the real ones.
    public AIUsageWidget(string? standaloneMutexName, string? widgetMutexName, string? presenceMutexName = null)
    {
        // Tests build widgets with private mutex names and must not put a topmost window on the screen.
        _showChips = standaloneMutexName == null && widgetMutexName == null;
        _presenceMutexName = presenceMutexName ?? (standaloneMutexName == null && widgetMutexName == null
            ? AppIdentity.WidgetPresenceMutexName
            : "Local\\AIUsage-Widget-Presence-" + Guid.NewGuid().ToString("N"));
        _standaloneMutexName = standaloneMutexName ?? AppIdentity.StandaloneMutexName;
        _widgetMutexName = widgetMutexName ?? AppIdentity.WidgetMutexName;
    }

    public WidgetManifest Manifest { get; } = AIUsageWidgetManifest.Create();

    public Task InitializeAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        // One owner of the mini widget: refuse to appear next to a running app, except when that app's widget is
        // being dropped onto the Host. (The Host logs the refusal and skips the widget, without a dialog.)
        if (!DockArrival.InProgress && AppIdentity.IsHeldByAnotherProcess(_standaloneMutexName))
            throw new WidgetRefusedException("AI Usage is running as its own application; only one owner may show the widget.");

        _context = context;
        // Handlers reach the view model lazily: it is only built once the UI thread asks for a view.
        _subscriptions.Add(context.Commands.Register(AIUsageWidgetManifest.RefreshCommand, async (_, _) =>
        {
            if (_viewModel != null) await _viewModel.RefreshAsync(force: true);
        }));
        _subscriptions.Add(context.Commands.Register(AIUsageWidgetManifest.SelectAccountCommand, (arg, _) =>
        {
            if (_viewModel != null && arg is string key)
            {
                _viewModel.SelectedAccount = _viewModel.Providers.FirstOrDefault(p => p.AccountKey == key) ?? _viewModel.SelectedAccount;
            }
            return Task.CompletedTask;
        }));
        return Task.CompletedTask;
    }

    public object CreateSummaryView(IWidgetContext context)
    {
        EnsureViewModel(context);
        return new HostSummaryView { DataContext = _viewModel };
    }

    public object? CreateDetailView(IWidgetContext context)
    {
        EnsureViewModel(context);
        var view = new UsageDetailView { DataContext = _viewModel };
        HostThemeBridge.ApplyFont(view);
        view.AddFeatureSettingsTab();
        // Same layout as the standalone window: banner on top, the dashboard below, an outer margin around both.
        var header = new UsageDashboardHeader { DataContext = _viewModel };
        HostThemeBridge.ApplyFont(header);
        var root = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(22) };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        view.Margin = new System.Windows.Thickness(0, 20, 0, 0);
        System.Windows.Controls.Grid.SetRow(view, 1);
        HostThemeBridge.Watch(root);
        root.Children.Add(header);
        root.Children.Add(view);
        return root;
    }

    public async Task SaveStateAsync(IWidgetStateWriter writer)
    {
        // Before any view exists there is nothing new to persist: hand back what was restored.
        var snapshot = _store?.Latest ?? _restored;
        if (snapshot == null) return;

        // The snapshot shares the live collections the UI thread mutates; serialize where they are owned.
        var json = _dispatcher != null && !_dispatcher.CheckAccess()
            ? await _dispatcher.InvokeAsync(snapshot.Serialize)
            : snapshot.Serialize();
        writer.Write(FeatureStateSnapshot.CurrentVersion, json);
    }

    public Task RestoreStateAsync(IWidgetStateReader reader)
    {
        _restored = reader.TryRead(out var version, out var json)
            ? FeatureStateSnapshot.Deserialize(version, json)
            : null;
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();

        // Timers and the collection mutex belong to the UI thread that created them; releasing the mutex from
        // another thread would leave it held and block the next instance from ever collecting.
        if (_dispatcher != null && !_dispatcher.CheckAccess()) await _dispatcher.InvokeAsync(Teardown);
        else Teardown();
    }

    private void Teardown()
    {
        _ownershipTimer?.Stop();
        _ownershipTimer = null;
        _ownership?.Dispose();
        _ownership = null;
        if (_presence != null)
        {
            try { _presence.ReleaseMutex(); }
            catch (ApplicationException) { }
            _presence.Dispose();
            _presence = null;
        }
        _chips?.CloseForExit();
        _chips = null;
        _viewModel?.Dispose();
        _viewModel = null;
        _store = null;
    }

    // Must run on the UI thread (WPF objects, dispatcher timers).
    private void EnsureViewModel(IWidgetContext context)
    {
        if (_viewModel != null) return;

        _dispatcher = Dispatcher.CurrentDispatcher;
        // Announce the widget's existence so the app refuses a plain start while it is in the Host.
        _presence = new Mutex(true, _presenceMutexName, out var presenceCreated);
        if (!presenceCreated) { _presence.Dispose(); _presence = null; }
        PresentationResources.EnsureThemeDefaults();
        HostThemeBridge.Apply();
        _store = new HostStateStore(_restored, new StateStore());
        var ui = new HostUiServices(context.Permissions);
        var viewModel = new UsageFeatureViewModel(_store, ui);
        _viewModel = viewModel;

        viewModel.NotificationRequested += (title, message, _) => context.Notifications.Notify(title, message);
        viewModel.Refreshed += () => context.Events.Publish(AIUsageWidgetManifest.UsageUpdatedEvent);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UsageFeatureViewModel.SelectedAccount))
                context.Events.Publish(AIUsageWidgetManifest.ProviderChangedEvent, viewModel.SelectedAccount?.AccountKey);
        };

        StartOwnership(viewModel);
        StartChips(viewModel);
        _ = RequestPermissionsAsync(context, viewModel);
    }

    // The taskbar chips are part of the feature: while this widget owns it they show from the same setting, and a
    // click on them opens the Host's detail window (the one main screen).
    private void StartChips(UsageFeatureViewModel viewModel)
    {
        if (!_showChips) return;
        void Apply()
        {
            if (viewModel.ShowTaskbarChips)
            {
                _chips ??= new UsageChipsWindow(viewModel, new ChipsActions { Click = OpenDetail, OpenDashboard = OpenDetail });
                _chips.ShowChips();
            }
            else _chips?.Hide();
        }
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UsageFeatureViewModel.ShowTaskbarChips)) Apply();
        };
        Apply();
    }

    private static void OpenDetail() => AppIdentity.TryOpenHostDetail();

    // One collector at a time: yield to the standalone app (or another widget process) and take over when it leaves.
    private void StartOwnership(UsageFeatureViewModel viewModel)
    {
        _ownership = new CollectionOwnership(isStandalone: false, _standaloneMutexName, _widgetMutexName);
        void Check()
        {
            _ownership.Evaluate();
            // A hand-over can create this widget before the previous instance released the presence mutex.
            if (_presence == null)
            {
                _presence = new Mutex(true, _presenceMutexName, out var created);
                if (!created) { _presence.Dispose(); _presence = null; }
            }
            viewModel.SetCollectionSuspended(!_ownership.MayCollect);
        }

        Check();
        _ = viewModel.RefreshAsync();
        _ownershipTimer = new DispatcherTimer { Interval = OwnershipPoll };
        _ownershipTimer.Tick += (_, _) => Check();
        _ownershipTimer.Start();
    }

    private static async Task RequestPermissionsAsync(IWidgetContext context, UsageFeatureViewModel viewModel)
    {
        try
        {
            // One prompt for both: reading the CLI logins and calling the usage endpoints are one decision.
            await context.Permissions.RequestAsync(WidgetCapabilities.FileSystem | WidgetCapabilities.Network);
        }
        catch (Exception ex)
        {
            context.Logger.Log("Warn", "permission request failed", ex);
        }
        viewModel.NotifyPermissionsChanged();
    }
}
