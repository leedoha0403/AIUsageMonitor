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
    private Dispatcher? _dispatcher;

    // The Host's plugin loader only registers widgets with a truly parameterless constructor (optional
    // parameters do not count), so this one must stay.
    public AIUsageWidget() : this(null, null)
    {
    }

    // Mutex names are overridable so tests (and side-by-side installs) do not clash with the real ones.
    public AIUsageWidget(string? standaloneMutexName, string? widgetMutexName)
    {
        _standaloneMutexName = standaloneMutexName ?? AppIdentity.StandaloneMutexName;
        _widgetMutexName = widgetMutexName ?? AppIdentity.WidgetMutexName;
    }

    public WidgetManifest Manifest { get; } = AIUsageWidgetManifest.Create();

    public Task InitializeAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
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
        view.AddFeatureSettingsTab();
        return view;
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
        _viewModel?.Dispose();
        _viewModel = null;
        _store = null;
    }

    // Must run on the UI thread (WPF objects, dispatcher timers).
    private void EnsureViewModel(IWidgetContext context)
    {
        if (_viewModel != null) return;

        _dispatcher = Dispatcher.CurrentDispatcher;
        PresentationResources.EnsureThemeDefaults();
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
        _ = RequestPermissionsAsync(context, viewModel);
    }

    // One collector at a time: yield to the standalone app (or another widget process) and take over when it leaves.
    private void StartOwnership(UsageFeatureViewModel viewModel)
    {
        _ownership = new CollectionOwnership(isStandalone: false, _standaloneMutexName, _widgetMutexName);
        void Check()
        {
            _ownership.Evaluate();
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
