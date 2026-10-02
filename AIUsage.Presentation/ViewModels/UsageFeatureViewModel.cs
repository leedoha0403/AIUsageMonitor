using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using AIUsage.Presentation.Controls;
using AIUsage.Core;
using AIUsage.Presentation;
using AIUsage.Core.Providers;
using AIUsage.Core.Refresh;
using AIUsage.Core.Storage;

namespace AIUsage.Presentation.ViewModels;

public class UsageFeatureViewModel : ObservableObject, IDisposable
{

    private static readonly Dictionary<string, TimeSpan> RangeSpans = new()
    {
        ["1H"] = TimeSpan.FromHours(1),
        ["6H"] = TimeSpan.FromHours(6),
        ["1D"] = TimeSpan.FromDays(1),
        ["3D"] = TimeSpan.FromDays(3),
        ["7D"] = TimeSpan.FromDays(7),
        ["30D"] = TimeSpan.FromDays(30)
    };

    private static readonly string[] ChartModes = ["Session", "Weekly"];

    protected readonly IUsageStore _store;
    private readonly UsageAggregator _aggregator = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private string _diagnosticsText = "";
    private string _historyText = "";
    private string _language;
    private string _displayUsageAs;
    private string _collectionLevel;
    private int _refreshSeconds;
    private string _historyRange;
    private string _historyChartMode;
    private string _thresholdsText;
    private IReadOnlyList<ChartSeries> _chartSeries = [];
    private IReadOnlyList<ChartArea> _chartAreas = [];
    private string? _highlightedSeriesName;
    private bool _isRefreshing;
    private int _tick;
    private ProviderViewModel? _selectedAccount;
    private int _selectedTabIndex;

    private readonly IUiServices _ui;

    public UsageFeatureViewModel(IUsageStore store, IUiServices ui)
    {
        _store = store;
        _ui = ui;
        State = _store.LoadState();
        Providers = new ObservableCollection<ProviderViewModel>();
        ActiveProviders = new ObservableCollection<ProviderViewModel>();
        MiniProviders = new ObservableCollection<ProviderViewModel>();
        DashboardProviders = new ObservableCollection<ProviderViewModel>();
        _language = State.Settings.Language;
        _displayUsageAs = State.Settings.DisplayUsageAs;
        _widgetMode = WidgetModeValues.Contains(State.Settings.WidgetMode) ? State.Settings.WidgetMode : "Normal";
        _collectionLevel = State.Settings.CollectionLevel;
        _refreshSeconds = State.Settings.RefreshSeconds;
        _historyRange = RangeSpans.ContainsKey(State.Settings.HistoryRange) ? State.Settings.HistoryRange : "1D";
        _historyChartMode = ChartModes.Contains(State.Settings.HistoryChartMode) ? State.Settings.HistoryChartMode : "Session";
        _thresholdsText = string.Join(", ", State.Settings.NotificationThresholds);
        RebuildProviders();

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(force: true));
        SaveCommand = new RelayCommand(SaveManualSnapshot);
        AddAccountCommand = new ParamCommand(p => AddAccount(p as string ?? "claude"));
        RemoveAccountCommand = new ParamCommand(p => RemoveAccount(p as ProviderViewModel));
        SetPrimaryCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) SetPrimary(vm); });
        BrowseFolderCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) BrowseFolder(vm); });
        ClearFolderCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) vm.ConfigDirectory = ""; });
        OpenAccountCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) { SelectedAccount = vm; SelectedTabIndex = 2; } });
        ShowInOverviewCommand = new RelayCommand(() => SelectedTabIndex = 0);
        LoginCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) StartLogin(vm); });
        _loginTimer.Tick += (_, _) => CheckLoginWatches();
        OpenScheduleCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) OpenSchedule(vm); });
        ToggleNotifyCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) vm.NotifyOnReset = !vm.NotifyOnReset; });

        // Scheduled refresh: separate runner/scheduler, checked every 15 seconds and right after the PC wakes.
        _runner = new RefreshRunner(_store.DataDirectory);
        _scheduler = new RefreshScheduler(State, _runner,
            force => RefreshAsync(force),
            (title, body) => RaiseNotification(title, body),
            OnRefreshStateChanged);
        CancelScheduleCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) { _scheduler.Cancel(vm.State); } });
        RetryRefreshCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) { _scheduler.Retry(vm.State); _ = TickSchedulerAsync(); } });
        SetHistoryChartModeCommand = new ParamCommand(p => { if (p is string mode) HistoryChartMode = mode; });
        ProviderViewModel.RetryMaxForDisplay = State.Settings.Refresh.RetryMax;
        _schedulerTimer.Tick += (_, _) => _ = TickSchedulerAsync();
        _schedulerTimer.Start();
        // First check shortly after launch instead of waiting a full interval.
        _firstTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _firstTick.Tick += (_, _) => { _firstTick.Stop(); _ = TickSchedulerAsync(); };
        _firstTick.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        LocResources.Apply(_language);

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) =>
        {
            if (!_presented) return;
            var full = ++_tick % 30 == 0;
            // The clock only moves the time-dependent properties; the full pass every 30 s is a safety net.
            if (full) RefreshDerived();
            else RefreshTime();
            if (full) RefreshAnalytics();
        };
        _countdownTimer.Start();
        ConfigureRefreshTimer();
        RefreshTextViews();
    }

    private readonly RefreshRunner _runner;
    private readonly RefreshScheduler _scheduler;
    private readonly DispatcherTimer _firstTick;
    private bool _disposed;
    private readonly DispatcherTimer _schedulerTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    public ICommand OpenScheduleCommand { get; }
    public ICommand CancelScheduleCommand { get; }
    public ICommand RetryRefreshCommand { get; }
    public ICommand ToggleNotifyCommand { get; }

    // ---- Scheduled refresh settings (Refresh tab)

    public IReadOnlyList<OptionItem> PreNotifyOptions { get; } = new[] { 0, 5, 10, 15, 30 }
        .Select(n => new OptionItem(n, () => n == 0 ? Loc.T("rf.set.preOff") : Loc.T("rf.set.preMin", n))).ToList();
    public IReadOnlyList<OptionItem> RetryIntervalOptions { get; } = new[] { 1, 2, 5, 10 }
        .Select(n => new OptionItem(n, () => Loc.T("rf.set.minutes", n))).ToList();
    public IReadOnlyList<OptionItem> RetryMaxOptions { get; } = new[] { 1, 2, 3, 5 }
        .Select(n => new OptionItem(n, () => Loc.T("rf.set.times", n))).ToList();

    public int PreNotifyMinutes
    {
        get => State.Settings.Refresh.PreNotifyMinutes;
        set { State.Settings.Refresh.PreNotifyMinutes = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public bool RetryEnabled
    {
        get => State.Settings.Refresh.RetryEnabled;
        set { State.Settings.Refresh.RetryEnabled = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public int RetryIntervalMinutes
    {
        get => State.Settings.Refresh.RetryIntervalMinutes;
        set { State.Settings.Refresh.RetryIntervalMinutes = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public int RetryMax
    {
        get => State.Settings.Refresh.RetryMax;
        set
        {
            State.Settings.Refresh.RetryMax = value;
            ProviderViewModel.RetryMaxForDisplay = value;
            OnPropertyChanged();
            SaveStateOnly();
        }
    }

    public bool MissedRunOnWake
    {
        get => State.Settings.Refresh.MissedPolicy == "RunOnWake";
        set { if (value) SetMissedPolicy("RunOnWake"); }
    }

    public bool MissedWaitNext
    {
        get => State.Settings.Refresh.MissedPolicy == "WaitNext";
        set { if (value) SetMissedPolicy("WaitNext"); }
    }

    public bool MissedSkip
    {
        get => State.Settings.Refresh.MissedPolicy == "Skip";
        set { if (value) SetMissedPolicy("Skip"); }
    }

    public string RefreshLogText => State.RefreshLog.Count == 0
        ? Loc.T("rf.set.noLog")
        : string.Join(Environment.NewLine, State.RefreshLog.AsEnumerable().Reverse().Take(40).Select(l =>
            $"{l.At.ToLocalTime():MM-dd HH:mm:ss}  {Loc.Term("rf.outcome.", l.Outcome),-4}  {l.Title}{(l.DurationMs > 0 ? $"  {l.DurationMs} ms" : "")}{(string.IsNullOrEmpty(l.Detail) ? "" : Environment.NewLine + "      " + l.Detail)}"));

    private void SetMissedPolicy(string policy)
    {
        State.Settings.Refresh.MissedPolicy = policy;
        OnPropertyChanged(nameof(MissedRunOnWake));
        OnPropertyChanged(nameof(MissedWaitNext));
        OnPropertyChanged(nameof(MissedSkip));
        SaveStateOnly();
    }

    private void OpenSchedule(ProviderViewModel provider)
    {
        var editor = new ScheduleEditorViewModel(provider, State.Settings.Refresh);
        if (!_ui.EditSchedule(editor)) return;
        if (editor.Result == ScheduleEditorResult.Schedule)
        {
            editor.ApplyTo(provider.State.Refresh);
            _scheduler.Arm(provider.State);
            _ = TickSchedulerAsync();
        }
        else if (editor.Result == ScheduleEditorResult.Unschedule)
        {
            _scheduler.Cancel(provider.State);
        }
    }

    private void OnRefreshStateChanged()
    {
        SaveStateOnly();
        foreach (var provider in Providers) provider.RefreshDerived();
        OnPropertyChanged(nameof(RefreshLogText));
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        // Give the network a moment after waking, then apply the missed-time policy.
        _ui.Post(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                if (!_disposed) await TickSchedulerAsync();
            }
            catch (Exception ex)
            {
                AppLog.Write("resume tick failed: " + ex.Message);
            }
        });
    }

    // title, body, optional action when the notification is clicked
    public event Action<string, string, Action?>? NotificationRequested;
    private readonly HashSet<string> _nudged = new();
    // Asks the window layer to bring up the widget, where the login button lives.
    public event Action? LoginPromptRequested;
    protected void RaiseNotification(string title, string message, Action? onClick = null)
    {
        if (_collectionSuspended) return;
        NotificationRequested?.Invoke(title, message, onClick);
    }
    private readonly Dictionary<string, (bool Install, DateTimeOffset Baseline, DateTimeOffset Until)> _loginWatches = new();
    private readonly DispatcherTimer _loginTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public AppState State { get; private set; }
    public ObservableCollection<ProviderViewModel> Providers { get; }
    // Collected accounts (monitoring on), and the subsets shown in the mini widget/chips and the dashboard.
    public ObservableCollection<ProviderViewModel> ActiveProviders { get; }
    public ObservableCollection<ProviderViewModel> MiniProviders { get; }
    public ObservableCollection<ProviderViewModel> DashboardProviders { get; }
    public IReadOnlyList<OptionItem> Languages { get; } = Options("Korean", "English");
    public IReadOnlyList<OptionItem> DisplayOptions { get; } = Options("Remaining", "Used");
    public IReadOnlyList<OptionItem> CollectionLevels { get; } = Options(CollectorPolicy.Levels);
    public IReadOnlyList<OptionItem> HistoryRanges { get; } = Options(RangeSpans.Keys.ToArray());
    public IReadOnlyList<OptionItem> ProviderIds { get; } = Options(Defaults.ProviderOrder);
    public IReadOnlyList<OptionItem> RefreshIntervals { get; } = new[] { 0, 15, 30, 60, 120, 300, 900 }
        .Select(n => new OptionItem(n, () => n == 0 ? Loc.T("opt.refresh.0") : n < 60 ? Loc.T("opt.refresh.sec", n) : Loc.T("opt.refresh.min", n / 60)))
        .ToList();
    public ICommand RefreshCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddAccountCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand SetPrimaryCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand ClearFolderCommand { get; }
    public ICommand OpenAccountCommand { get; }
    public ICommand ShowInOverviewCommand { get; }

    public ProviderViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set => Set(ref _selectedAccount, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }
    public ICommand LoginCommand { get; }
    public string DataDirectory => _store.DataDirectory;
    public virtual string InternalVersion { get; } = typeof(UsageFeatureViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "0.0.0-internal";
    public string RuntimeVersion => Environment.Version.ToString();
    public string StateSchemaVersion => $"v{new AppState().SchemaVersion}";

    public string Language
    {
        get => _language;
        set
        {
            if (value != null && Set(ref _language, value))
            {
                State.Settings.Language = value;
                SaveStateOnly();
                OnLanguageChanged();
            }
        }
    }

    public string DisplayUsageAs
    {
        get => _displayUsageAs;
        set { if (value != null && Set(ref _displayUsageAs, value)) { State.Settings.DisplayUsageAs = value; SaveStateOnly(); OnPropertyChanged(nameof(ShowRemaining)); OnPropertyChanged(nameof(DisplayUsageLabel)); } }
    }

    public string CollectionLevel
    {
        get => _collectionLevel;
        set
        {
            if (value != null && Set(ref _collectionLevel, value))
            {
                State.Settings.CollectionLevel = value;
                SaveStateOnly();
                OnLanguageChanged();
                _ = RefreshAsync(force: true);
            }
        }
    }

    public int RefreshSeconds
    {
        get => _refreshSeconds;
        set { if (Set(ref _refreshSeconds, value)) { State.Settings.RefreshSeconds = value; ConfigureRefreshTimer(); SaveStateOnly(); } }
    }

    public event Action? LanguageChanged;

    public bool NotificationsEnabled
    {
        get => State.Settings.NotificationsEnabled;
        set { State.Settings.NotificationsEnabled = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public string FavoriteProvider
    {
        get => State.Settings.FavoriteProvider;
        set
        {
            if (value == null || State.Settings.FavoriteProvider == value) return;
            State.Settings.FavoriteProvider = value;
            OnPropertyChanged();
            SaveStateOnly();
            RebuildProviders();
        }
    }

    public string ThresholdsText
    {
        get => _thresholdsText;
        set
        {
            if (!Set(ref _thresholdsText, value)) return;
            var parsed = value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.TrimEnd('%'), out var n) ? n : -1)
                .Where(n => n is > 0 and <= 100)
                .Distinct()
                .Order()
                .ToList();
            if (parsed.Count == 0) return;
            State.Settings.NotificationThresholds = parsed;
            SaveStateOnly();
        }
    }

    public string HistoryRange
    {
        get => _historyRange;
        set
        {
            if (value == null || !RangeSpans.ContainsKey(value) || !Set(ref _historyRange, value)) return;
            State.Settings.HistoryRange = value;
            SaveStateOnly();
            OnPropertyChanged(nameof(ChartRange));
            HistoryText = BuildHistoryText();
            BuildHistoryChart();
        }
    }

    public TimeSpan ChartRange => RangeSpans[_historyRange];

    public string HistoryChartMode
    {
        get => _historyChartMode;
        set
        {
            if (value == null || !ChartModes.Contains(value) || !Set(ref _historyChartMode, value)) return;
            State.Settings.HistoryChartMode = value;
            SaveStateOnly();
            OnPropertyChanged(nameof(HistoryChartDescription));
            OnPropertyChanged(nameof(IsSessionChartMode));
            OnPropertyChanged(nameof(IsCumulativeChartMode));
            BuildHistoryChart();
        }
    }

    // One-click segmented selector for HistoryChartMode (see MainWindow.xaml SegmentButton), instead of a
    // dropdown that needs opening first.
    public ICommand SetHistoryChartModeCommand { get; }
    public bool IsSessionChartMode => _historyChartMode == "Session";
    public bool IsCumulativeChartMode => _historyChartMode == "Weekly";

    public string HistoryChartDescription => _historyChartMode == "Session" ? Loc.T("ui.usageHistoryDesc") : Loc.T("ui.weeklyUsageHistoryDesc");

    // The 5H (or own) usage line per provider — only drawn while in Session mode.
    public IReadOnlyList<ChartSeries> ChartSeries
    {
        get => _chartSeries;
        private set => Set(ref _chartSeries, value);
    }

    // Cumulative usage, filled and split into one block per 5H session, per provider — only built in
    // Weekly/cumulative mode.
    public IReadOnlyList<ChartArea> ChartAreas
    {
        get => _chartAreas;
        private set => Set(ref _chartAreas, value);
    }

    // Set while hovering a legend entry, so the chart can fade every other provider and bring this one forward.
    public string? HighlightedSeriesName
    {
        get => _highlightedSeriesName;
        set => Set(ref _highlightedSeriesName, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => Set(ref _isRefreshing, value);
    }

    // Content density of the mini summary (Compact / Normal / Detailed). A feature option that every surface
    // (standalone mini window, Host-docked Natural view) honors; it is persisted with the feature state.
    private static readonly string[] WidgetModeValues = ["Compact", "Normal", "Detailed"];
    private string _widgetMode;
    public IReadOnlyList<OptionItem> WidgetModes { get; } = Options("Compact", "Normal", "Detailed");

    public string WidgetMode
    {
        get => _widgetMode;
        set
        {
            if (value == null || !WidgetModeValues.Contains(value) || !Set(ref _widgetMode, value)) return;
            State.Settings.WidgetMode = value;
            SaveStateOnly();
            OnPropertyChanged(nameof(IsCompact));
            OnPropertyChanged(nameof(IsDetailed));
        }
    }

    public bool IsCompact => WidgetMode == "Compact";
    public bool IsDetailed => WidgetMode == "Detailed";
    public bool ShowRemaining => DisplayUsageAs == "Remaining";
    // The single account a collapsed summary shows: the primary one, else the first visible.
    public ProviderViewModel? SummaryProvider => MiniProviders.FirstOrDefault(p => p.IsPrimary) ?? MiniProviders.FirstOrDefault();

    private void OnStatusSummaryChanged()
    {
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(SummaryProvider));
    }

    public string StatusSummary => string.Join(Environment.NewLine, MiniProviders.Select(p => $"{p.Title}: {p.Summary}"));
    public string AppTitle => Loc.T("ui.appName");
    public string AppSubtitle => Loc.T("mv.subtitle");
    public string DisplayUsageLabel => Loc.Term("opt.", DisplayUsageAs);
    public string CollectionLevelLabel => Loc.Term("opt.", CollectionLevel);

    public string FetchWarning => Loc.T("mv.warn." + CollectionLevel);

    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set => Set(ref _diagnosticsText, value);
    }

    public string HistoryText
    {
        get => _historyText;
        private set => Set(ref _historyText, value);
    }

    // Stops every timer and event hook so a hosted instance leaves nothing running after it is removed.
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _countdownTimer.Stop();
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimerOnTick;
        _schedulerTimer.Stop();
        _firstTick.Stop();
        _loginTimer.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    // ---- Ownership hand-over: the feature state that travels between a Host widget and the standalone app.

    // Snapshot of everything the feature owns (accounts, settings, refresh log); shell/Host values are excluded.
    // ---- Taskbar chips (feature state: the owner of the widget shows them)

    public bool ShowTaskbarChips
    {
        get => State.Settings.ShowTaskbarChips;
        set { State.Settings.ShowTaskbarChips = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public double ChipsOpacity
    {
        get => State.Settings.ChipsOpacity;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.2, 1.0), 2);
            if (Math.Abs(State.Settings.ChipsOpacity - clamped) < 0.001) return;
            State.Settings.ChipsOpacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChipsOpacityText));
            SaveStateOnly();
        }
    }

    public string ChipsOpacityText => $"{ChipsOpacity * 100:0}%";

    public void SaveChipsPlacement(double left, double top)
    {
        State.ChipsLeft = left;
        State.ChipsTop = top;
        SaveStateOnly();
    }

    // The chips settings of another owner win even when the rest of its state is not taken over (an app that was
    // already running keeps its own, fresher, accounts).
    public void AdoptChipsSettings(FeatureStateSnapshot snapshot)
    {
        State.ChipsLeft = snapshot.Settings.ChipsLeft;
        State.ChipsTop = snapshot.Settings.ChipsTop;
        ChipsOpacity = snapshot.Settings.ChipsOpacity;
        ShowTaskbarChips = snapshot.Settings.ShowTaskbarChips;
    }

    // The collected numbers and their provenance; account settings (name, visibility, schedule) stay as they were.
    private static void CopyCollectedUsage(UsageProviderState from, UsageProviderState to)
    {
        to.Plan = from.Plan;
        to.Status = from.Status;
        to.Source = from.Source;
        to.Confidence = from.Confidence;
        to.SessionUsagePercent = from.SessionUsagePercent;
        to.SessionResetAt = from.SessionResetAt;
        to.WeeklyUsagePercent = from.WeeklyUsagePercent;
        to.WeeklyResetAt = from.WeeklyResetAt;
        to.ExtraUsageCost = from.ExtraUsageCost;
        to.ExtraUsage = from.ExtraUsage;
        to.CreditsBalance = from.CreditsBalance;
        to.ModelBreakdown = from.ModelBreakdown;
        to.CollectedAt = from.CollectedAt;
        to.LastSuccessAt = from.LastSuccessAt;
        to.SessionResetObservedAt = from.SessionResetObservedAt;
        to.ConsecutiveFailures = from.ConsecutiveFailures;
        to.Message = from.Message;
        to.LastNotifiedThreshold = from.LastNotifiedThreshold;
        to.NotifiedWindowResetAt = from.NotifiedWindowResetAt;
        to.FieldSources = from.FieldSources;
        to.Collectors = from.Collectors;
    }

    public FeatureStateSnapshot CaptureFeatureState() => FeatureStateSnapshot.Capture(State);

    // Takes over another instance's feature state in place (the scheduler and view models keep their references).
    public void AdoptFeatureState(FeatureStateSnapshot snapshot)
    {
        var incoming = snapshot.ToAppState();

        var previous = State.Providers.ToDictionary(p => p.Key, p => p.Value);
        State.Providers.Clear();
        foreach (var (key, account) in incoming.Providers)
        {
            // An older copy must not send the numbers back in time: keep what this side collected more recently.
            if (previous.TryGetValue(key, out var current) && current.LastSuccessAt > (account.LastSuccessAt ?? DateTimeOffset.MinValue))
                CopyCollectedUsage(current, account);
            State.Providers[key] = account;
        }
        State.RefreshLog.Clear();
        State.RefreshLog.AddRange(incoming.RefreshLog);

        var settings = State.Settings;
        var next = incoming.Settings;
        settings.FavoriteAccount = next.FavoriteAccount;
        settings.WarningThreshold = next.WarningThreshold;
        settings.AllowUnverifiedCollectors = next.AllowUnverifiedCollectors;
        settings.Refresh = next.Refresh;
        ProviderViewModel.RetryMaxForDisplay = settings.Refresh.RetryMax;

        // Through the properties, so backing fields, side effects and persistence stay consistent.
        Language = next.Language;
        DisplayUsageAs = next.DisplayUsageAs;
        RefreshSeconds = next.RefreshSeconds;
        NotificationsEnabled = next.NotificationsEnabled;
        ThresholdsText = string.Join(", ", next.NotificationThresholds);
        HistoryRange = next.HistoryRange;
        HistoryChartMode = next.HistoryChartMode;
        FavoriteProvider = next.FavoriteProvider;
        CollectionLevel = next.CollectionLevel;
        State.ChipsLeft = incoming.ChipsLeft;
        State.ChipsTop = incoming.ChipsTop;
        ChipsOpacity = next.ChipsOpacity;
        ShowTaskbarChips = next.ShowTaskbarChips;

        RebuildProviders();
        SaveStateOnly();
        RefreshTextViews();
        foreach (var name in new[] { nameof(PreNotifyMinutes), nameof(RetryEnabled), nameof(RetryIntervalMinutes), nameof(RetryMax),
                                     nameof(MissedRunOnWake), nameof(MissedWaitNext), nameof(MissedSkip), nameof(RefreshLogText) })
        {
            OnPropertyChanged(name);
        }
    }

    // ---- Collection gate: another instance may already be collecting, or the host may not have granted access yet.

    private bool _collectionSuspended;

    public event Action? Refreshed;

    public bool IsCollectionSuspended => _collectionSuspended;

    private bool HasCollectionPermissions => _ui.IsGranted(UsageCapability.FileSystem) && _ui.IsGranted(UsageCapability.Network);

    public bool CanCollect => !_collectionSuspended && HasCollectionPermissions;

    // Shown next to the data when it is not being refreshed by this instance.
    public string CollectionNotice => _collectionSuspended
        ? Loc.T("ui.collectionSuspended")
        : HasCollectionPermissions ? "" : Loc.T("ui.collectionNoPermission");

    public bool HasCollectionNotice => CollectionNotice.Length > 0;

    // Suspended: no polling, scheduled runs, notifications or history writes (another instance owns collection).
    public void SetCollectionSuspended(bool suspended)
    {
        if (!Set(ref _collectionSuspended, suspended, nameof(IsCollectionSuspended))) return;
        RaiseCollectionStateChanged();
        if (!suspended) _ = RefreshAsync();
    }

    // Call after the host changed what it grants.
    public void NotifyPermissionsChanged()
    {
        RaiseCollectionStateChanged();
        if (CanCollect) _ = RefreshAsync();
    }

    private void RaiseCollectionStateChanged()
    {
        OnPropertyChanged(nameof(CanCollect));
        OnPropertyChanged(nameof(CollectionNotice));
        OnPropertyChanged(nameof(HasCollectionNotice));
    }

    // Scheduled refreshes run the provider CLI through RefreshRunner, so they also need ProcessExecution.
    public bool CanRunScheduledRefresh => CanCollect && _ui.IsGranted(UsageCapability.ProcessExecution);

    private Task TickSchedulerAsync() => CanRunScheduledRefresh ? _scheduler.TickAsync(DateTimeOffset.Now) : Task.CompletedTask;

    public async Task RefreshAsync(bool force = false)
    {
        if (IsRefreshing || !CanCollect) return;
        IsRefreshing = true;
        try
        {
            // USAGE_MONITOR_DEMO=1 shows the saved state as-is (no collection), e.g. for screenshots.
            if (Environment.GetEnvironmentVariable("USAGE_MONITOR_DEMO") != "1") await _aggregator.RefreshAsync(State, force);
            _store.SaveState(State);
            _store.AppendHistory(State);
            foreach (var provider in Providers) provider.RefreshCollectors();
            RefreshDerived();
            RefreshTextViews();
            CheckThresholds();
            UpdateLoginState();
            NudgeSignedOut();
            Refreshed?.Invoke();
        }
        catch (Exception ex)
        {
            DiagnosticsText = Loc.T("mv.refreshFailed", ex) + Environment.NewLine + Environment.NewLine + BuildDiagnosticsText();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public void SaveManualSnapshot()
    {
        if (!CanCollect) return;
        foreach (var provider in Providers) provider.TouchManualSources();
        _store.SaveState(State);
        _store.AppendHistory(State, force: true);
        RefreshDerived();
        RefreshTextViews();
        CheckThresholds();
    }

    private void AddAccount(string providerId)
    {
        var key = $"{providerId}-{Guid.NewGuid().ToString("N")[..8]}";
        var number = State.Providers.Values.Count(a => a.ProviderId == providerId) + 1;
        State.Providers[key] = Defaults.CreateAccount(providerId, key, Loc.T("mv.newAccount", number));
        SaveStateOnly();
        RebuildProviders(key);
        SelectedTabIndex = 2;
        _ = RefreshAsync(force: true);
    }

    private void RemoveAccount(ProviderViewModel? account)
    {
        if (account == null || !account.CanRemove) return;
        if (!_ui.Confirm(Loc.T("mv.removeTitle"), Loc.T("mv.removeBody", account.Title))) return;
        State.Providers.Remove(account.AccountKey);
        if (State.Settings.FavoriteAccount == account.AccountKey) State.Settings.FavoriteAccount = "";
        SaveStateOnly();
        RebuildProviders(account.ProviderId);
        RefreshTextViews();
    }

    private void SetPrimary(ProviderViewModel account)
    {
        State.Settings.FavoriteAccount = account.AccountKey;
        State.Settings.FavoriteProvider = account.ProviderId;
        OnPropertyChanged(nameof(FavoriteProvider));
        SaveStateOnly();
        RebuildProviders(account.AccountKey);
    }

    private void BrowseFolder(ProviderViewModel account)
    {
        var initial = string.IsNullOrWhiteSpace(account.ConfigDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.ExpandEnvironmentVariables(account.ConfigDirectory);
        var picked = _ui.PickFolder(Loc.T("ui.acc.folder"), initial);
        if (picked != null) account.ConfigDirectory = picked;
    }

    // Account edits are applied immediately: names just save, enabling/folders re-collect.
    private void OnAccountEdited(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ProviderViewModel account) return;
        switch (e.PropertyName)
        {
            case nameof(ProviderViewModel.AccountName):
                SaveStateOnly();
                RefreshTextViews();
                break;
            case nameof(ProviderViewModel.IsEnabled):
                SaveStateOnly();
                SyncVisibleCollections();
                _ = RefreshAsync(force: true);
                break;
            case nameof(ProviderViewModel.ShowInMini):
            case nameof(ProviderViewModel.ShowInDashboard):
                SaveStateOnly();
                SyncVisibleCollections();
                break;
            case nameof(ProviderViewModel.NotifyOnReset):
                SaveStateOnly();
                break;
            case nameof(ProviderViewModel.ConfigDirectory):
            case nameof(ProviderViewModel.GitHubLogin):
                SaveStateOnly();
                _ = RefreshAsync(force: true);
                break;
        }
    }

    // selectKey: account key (or provider id) to select afterwards; defaults to the current selection.
    private void RebuildProviders(string? selectKey = null)
    {
        selectKey ??= SelectedAccount?.AccountKey;
        var primaryKey = State.Providers.TryGetValue(State.Settings.FavoriteAccount, out var favorite) && favorite.Enabled
            ? favorite.AccountKey
            : State.Providers.Values.Where(a => a.Enabled && a.ProviderId == State.Settings.FavoriteProvider)
                .OrderBy(a => Defaults.ProviderOrder.Contains(a.AccountKey) ? 0 : 1)
                .Select(a => a.AccountKey)
                .FirstOrDefault();
        var order = Defaults.ProviderOrder.ToList();
        var accounts = State.Providers.Values
            .OrderBy(a => a.AccountKey == primaryKey ? 0 : 1)
            .ThenBy(a => order.IndexOf(a.ProviderId))
            .ThenBy(a => Defaults.ProviderOrder.Contains(a.AccountKey) ? 0 : 1)
            .ThenBy(a => a.AccountName)
            .ToList();

        foreach (var old in Providers) old.PropertyChanged -= OnAccountEdited;
        Providers.Clear();
        ActiveProviders.Clear();
        MiniProviders.Clear();
        DashboardProviders.Clear();
        // Filled by SyncVisibleCollections below.
        var colorIndex = 0;
        foreach (var account in accounts)
        {
            var vm = new ProviderViewModel(account, colorIndex++)
            {
                ShowAccountName = accounts.Count(a => a.ProviderId == account.ProviderId) > 1,
                IsPrimary = account.AccountKey == primaryKey,
                IsLoginPending = _loginWatches.ContainsKey(account.AccountKey)
            };
            vm.PropertyChanged += OnAccountEdited;
            Providers.Add(vm);
        }
        SyncVisibleCollections();
        SelectedAccount = Providers.FirstOrDefault(p => p.AccountKey == selectKey)
                          ?? Providers.FirstOrDefault(p => p.ProviderId == selectKey)
                          ?? Providers.FirstOrDefault();
        UpdateLoginState();
        RefreshAnalytics();
        BuildHistoryChart();
        OnStatusSummaryChanged();
    }

    // Keeps the collected/mini/dashboard lists in step with each account's switches. Items are moved in place
    // (same view model instances), so toggling back and forth never rebinds the account being edited.
    private void SyncVisibleCollections()
    {
        Sync(ActiveProviders, Providers.Where(p => p.IsEnabled));
        Sync(MiniProviders, Providers.Where(p => p.IsEnabled && p.ShowInMini));
        Sync(DashboardProviders, Providers.Where(p => p.IsEnabled && p.ShowInDashboard));
        BuildHistoryChart();
        OnStatusSummaryChanged();
    }

    private static void Sync(ObservableCollection<ProviderViewModel> target, IEnumerable<ProviderViewModel> source)
    {
        var wanted = source.ToList();
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;
            var existing = target.IndexOf(wanted[i]);
            if (existing >= 0) target.Move(existing, i);
            else target.Insert(i, wanted[i]);
        }
    }

    // Notifies once per primary window (5H, or Copilot's month) for each newly crossed threshold (the highest one crossed).
    private void CheckThresholds()
    {
        if (!State.Settings.NotificationsEnabled) return;
        var changed = false;
        foreach (var provider in ActiveProviders.Where(p => p.ShowInMini || p.ShowInDashboard))
        {
            var account = provider.State;
            if (provider.IsSignedOut) continue;
            if (account.NotifiedWindowResetAt is not { } notified || (notified - provider.PrimaryResetAt).Duration() > TimeSpan.FromMinutes(10))
            {
                account.NotifiedWindowResetAt = provider.PrimaryResetAt;
                account.LastNotifiedThreshold = 0;
                changed = true;
            }
            var crossed = State.Settings.NotificationThresholds.Where(t => provider.PrimaryPercent >= t).DefaultIfEmpty(0).Max();
            if (crossed > account.LastNotifiedThreshold)
            {
                account.LastNotifiedThreshold = crossed;
                changed = true;
                RaiseNotification(
                    Loc.T("mv.notifyTitle", provider.Title, provider.PrimaryLabel, provider.PrimaryPercent),
                    Loc.T("mv.notifyBody", crossed, provider.Countdown, provider.SourceLine),
                    null);
            }
        }
        if (changed) SaveStateOnly();
    }

    // ---- Login guidance: open the CLI login (or installer) and wait for the credential file to appear.

    public void StartLogin(ProviderViewModel provider)
    {
        if (_ui.IsGranted(UsageCapability.ProcessExecution)) StartLoginCore(provider);
        else _ = StartLoginWhenGrantedAsync(provider);
    }

    private async Task StartLoginWhenGrantedAsync(ProviderViewModel provider)
    {
        if (await _ui.RequestAsync(UsageCapability.ProcessExecution)) StartLoginCore(provider);
    }

    private void StartLoginCore(ProviderViewModel provider)
    {
        var account = provider.State;
        if (LoginHelper.FindCli(account.ProviderId) != null)
        {
            if (!LoginHelper.LaunchLogin(account)) return;
            Watch(provider, install: false);
            return;
        }

        if (!_ui.Confirm(Loc.T("mv.installTitle", provider.DisplayName),
                Loc.T("mv.installBody", provider.DisplayName, LoginHelper.InstallCommand(account.ProviderId)))) return;
        LoginHelper.LaunchInstall(account.ProviderId);
        Watch(provider, install: true);
    }

    private void Watch(ProviderViewModel provider, bool install)
    {
        var credential = LoginHelper.CredentialPath(provider.State);
        var baseline = System.IO.File.Exists(credential) ? new DateTimeOffset(System.IO.File.GetLastWriteTime(credential)) : DateTimeOffset.MinValue;
        _loginWatches[provider.AccountKey] = (install, baseline, DateTimeOffset.Now.AddMinutes(15));
        provider.IsLoginPending = true;
        _loginTimer.Start();
    }

    private void CheckLoginWatches()
    {
        foreach (var (key, watch) in _loginWatches.ToList())
        {
            var provider = Providers.FirstOrDefault(p => p.AccountKey == key);
            if (provider == null || DateTimeOffset.Now > watch.Until)
            {
                _loginWatches.Remove(key);
                if (provider != null) provider.IsLoginPending = false;
                continue;
            }

            if (watch.Install)
            {
                if (LoginHelper.FindCli(provider.ProviderId) == null) continue;
                provider.CliInstalled = true;
                RaiseNotification(Loc.T("mv.installDetectedTitle", provider.DisplayName), Loc.T("mv.installDetectedBody"), null);
                _loginWatches.Remove(key);
                // Give the installer a moment to finish writing before chaining into the login.
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                delay.Tick += (_, _) => { delay.Stop(); StartLogin(provider); };
                delay.Start();
                continue;
            }

            var credential = LoginHelper.CredentialPath(provider.State);
            if (!System.IO.File.Exists(credential)) continue;
            if (new DateTimeOffset(System.IO.File.GetLastWriteTime(credential)) <= watch.Baseline) continue;
            _loginWatches.Remove(key);
            provider.IsLoginPending = false;
            _nudged.Remove(key);
            _ = LoginDetectedAsync(provider);
        }
        if (_loginWatches.Count == 0) _loginTimer.Stop();
    }

    private async Task LoginDetectedAsync(ProviderViewModel provider)
    {
        await RefreshAsync(force: true);
        if (!provider.IsSignedOut)
        {
            RaiseNotification(Loc.T("mv.loginDetectedTitle", provider.Title), Loc.T("mv.loginDetectedBody"), null);
        }
    }

    private void UpdateLoginState()
    {
        foreach (var provider in Providers)
        {
            if (provider.IsSignedOut) provider.CliInstalled = LoginHelper.FindCli(provider.ProviderId) != null;
        }
    }

    // One nudge per account per app session. Clicking it only opens the widget with the login button;
    // an external login process is started solely by an explicit button press.
    private void NudgeSignedOut()
    {
        foreach (var provider in ActiveProviders.Where(p => p.IsSignedOut && !p.IsLoginPending && (p.ShowInMini || p.ShowInDashboard)))
        {
            if (!_nudged.Add(provider.AccountKey)) continue;
            RaiseNotification(
                Loc.T("mv.nudgeTitle", provider.Title),
                provider.CliInstalled ? Loc.T("mv.nudgeBody") : Loc.T("mv.nudgeInstallBody"),
                () => LoginPromptRequested?.Invoke());
        }
    }

    private void ConfigureRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimerOnTick;
        if (RefreshSeconds <= 0) return;
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, RefreshSeconds));
        _refreshTimer.Tick += RefreshTimerOnTick;
        _refreshTimer.Start();
    }

    private void RefreshTimerOnTick(object? sender, EventArgs e) => _ = RefreshAsync();

    protected void SaveStateOnly() => _store.SaveState(State);

    private void RefreshDerived()
    {
        foreach (var provider in Providers) provider.RefreshDerived();
        OnStatusSummaryChanged();
    }

    private void RefreshTime()
    {
        foreach (var provider in Providers) provider.RefreshTime();
        OnStatusSummaryChanged();
    }

    // ---- Presentation hints
    // A shell that knows when nothing is on screen reports it here so the view model can skip work nobody sees.
    // A shell that never reports (a Host widget) stays "presented", which is the behaviour before these hints existed.
    private bool _presented = true;
    private bool _detailPresented = true;
    private bool _analyticsStale;
    private bool _textViewsStale;

    // Any surface showing usage (mini widget, chips, dashboard). Coming back brings everything up to date at once.
    public void SetPresented(bool presented)
    {
        if (_presented == presented) return;
        _presented = presented;
        if (!presented) return;
        RefreshDerived();
        if (_analyticsStale) RefreshAnalytics();
    }

    // The detail view (history chart, history and diagnostics text).
    public void SetDetailPresented(bool presented)
    {
        if (_detailPresented == presented) return;
        _detailPresented = presented;
        if (presented && _textViewsStale) RefreshTextViews();
    }

    private void RefreshAnalytics()
    {
        if (!_presented)
        {
            _analyticsStale = true;
            return;
        }
        _analyticsStale = false;
        var history = _store.LoadHistory();
        foreach (var provider in Providers) provider.UpdateAnalytics(history);
    }

    protected void RefreshTextViews()
    {
        // Velocity and forecast also feed the mini widget; everything else lives in the detail view only.
        RefreshAnalytics();
        if (!_detailPresented)
        {
            _textViewsStale = true;
            return;
        }
        _textViewsStale = false;
        DiagnosticsText = BuildDiagnosticsText();
        HistoryText = BuildHistoryText();
        BuildHistoryChart();
    }

    private void BuildHistoryChart()
    {
        if (!_detailPresented)
        {
            _textViewsStale = true;
            return;
        }
        var history = _store.LoadHistory();
        var since = DateTimeOffset.Now - ChartRange - ChartRange;

        // One pass over the history instead of one per account and series. Rows are appended in time order.
        var byAccount = new Dictionary<string, List<UsageSnapshot>>();
        foreach (var row in history)
        {
            if (row.Timestamp < since) continue;
            if (!byAccount.TryGetValue(row.EffectiveKey, out var rows)) byAccount[row.EffectiveKey] = rows = [];
            rows.Add(row);
        }

        List<(DateTimeOffset, double)> PointsFor(ProviderViewModel p, Func<UsageSnapshot, int> value) =>
            byAccount.TryGetValue(p.AccountKey, out var rows) ? rows.Select(x => (x.Timestamp, (double)value(x))).ToList() : [];

        var series = new List<ChartSeries>();
        var areas = new List<ChartArea>();
        foreach (var p in DashboardProviders)
        {
            // 5H (or own) usage line — drawn in Session mode, and always available for that card's legend.
            series.Add(new ChartSeries { Name = p.Title, Brush = p.SeriesBrush, Points = PointsFor(p, x => p.HasSessionWindow ? x.SessionUsagePercent : x.WeeklyUsagePercent) });

            // Cumulative usage, filled and split into one block per 5H session (providers without a 5H
            // window, e.g. Copilot, have nothing to split by, so their whole range is a single block).
            areas.Add(new ChartArea
            {
                Name = p.Title,
                Fill = WithOpacity(p.SeriesBrush, 0.45),
                Points = PointsFor(p, x => x.WeeklyUsagePercent),
                Boundaries = p.HasSessionWindow && byAccount.TryGetValue(p.AccountKey, out var accountRows) ? DetectSessionBoundaries(accountRows) : []
            });
        }
        ChartSeries = series;
        ChartAreas = areas;
    }

    // A meaningful drop in 5H usage between two consecutive polls means that window reset and a new one
    // began; each such point marks where one session's slice of the cumulative area ends and the next begins.
    private const int SessionResetDropThreshold = 5;

    // rows: one account's snapshots in time order (the history is kept sorted, new rows are appended).
    private static List<DateTimeOffset> DetectSessionBoundaries(IReadOnlyList<UsageSnapshot> rows)
    {
        var boundaries = new List<DateTimeOffset>();
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].SessionUsagePercent < rows[i - 1].SessionUsagePercent - SessionResetDropThreshold) boundaries.Add(rows[i].Timestamp);
        }
        return boundaries;
    }

    // A translucent version of a provider's own color for its cumulative area fill: same hue as its line
    // (so it's still obviously "that provider"), but see-through enough that two providers' areas overlapping
    // in time both stay visible instead of one flatly covering the other.
    private static System.Windows.Media.SolidColorBrush WithOpacity(System.Windows.Media.SolidColorBrush brush, double opacity)
    {
        var c = brush.Color;
        var faded = System.Windows.Media.Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B);
        var result = new System.Windows.Media.SolidColorBrush(faded);
        result.Freeze();
        return result;
    }

    private void OnLanguageChanged()
    {
        LocResources.Apply(Language);
        foreach (var option in new[] { Languages, DisplayOptions, WidgetModes, CollectionLevels, HistoryRanges, ProviderIds, RefreshIntervals }.SelectMany(x => x))
        {
            option.Refresh();
        }
        foreach (var option in PreNotifyOptions.Concat(RetryIntervalOptions).Concat(RetryMaxOptions)) option.Refresh();
        foreach (var name in new[] { nameof(AppTitle), nameof(AppSubtitle), nameof(FetchWarning), nameof(DisplayUsageLabel), nameof(CollectionLevelLabel), nameof(RefreshLogText), nameof(HistoryChartDescription), nameof(CollectionNotice) })
        {
            OnPropertyChanged(name);
        }
        OnShellLanguageChanged();
        RefreshDerived();
        RefreshTextViews();
        LanguageChanged?.Invoke();
    }

    // Lets a host shell refresh its own localized option lists/properties.
    protected virtual void OnShellLanguageChanged()
    {
    }

    // Shown on the diagnostics "Mode" line; the standalone shell reports its window version/density.
    protected virtual string ModeDescription => "Widget";

    protected static List<OptionItem> Options(params string[] values) =>
        values.Select(v => new OptionItem(v, () => Loc.Term("opt.", v))).ToList();

    private string BuildDiagnosticsText()
    {
        string L(string key) => Loc.T(key).PadRight(Loc.IsKorean ? 10 : 14);
        var lines = new List<string>
        {
            $"{Loc.T("ui.appName")} {InternalVersion}",
            $"{L("dg.runtime")}.NET {RuntimeVersion}",
            $"{L("dg.schema")}{StateSchemaVersion}",
            $"{L("dg.mode")}{ModeDescription}",
            $"{L("dg.collection")}{CollectionLevel}  {Loc.T("dg.collectionNote")}",
            $"{L("dg.wsl")}{(CredentialLocator.WslDistributions() is { Count: > 0 } d ? string.Join(", ", d) : Loc.T("dg.notDetected"))}",
            ""
        };
        foreach (var provider in Providers)
        {
            var a = provider.State;
            lines.Add($"{provider.Title}  [{a.AccountKey}]{(a.Enabled ? "" : "  " + Loc.T("dg.disabled"))}");
            lines.Add($"  {L("dg.health")}{provider.HealthText}");
            lines.Add($"  {L("dg.status")}{a.Status} · {Loc.Display(a.Message)}");
            lines.Add($"  {L("dg.configDir")}{(string.IsNullOrWhiteSpace(a.ConfigDirectory) ? Loc.T("dg.default") : a.ConfigDirectory)}");
            lines.Add($"  {L("dg.lastOk")}{Formatters.Ago(a.LastSuccessAt)}");
            foreach (var collector in a.Collectors)
            {
                var latency = collector.LatencyMs.HasValue ? $"{collector.LatencyMs.Value} ms" : "-";
                var tokenFree = collector.TokenFreeVerified ? "TOKEN-FREE" : "UNVERIFIED";
                lines.Add($"  {collector.Name,-10} {collector.Status,-13} {latency,-8} {tokenFree,-11} {collector.Level,-8} min {collector.MinIntervalSeconds}s · {Loc.T("dg.last")} {Formatters.Ago(collector.LastRunAt)}");
                if (!string.IsNullOrWhiteSpace(collector.Message)) lines.Add($"    {Loc.Display(collector.Message)}");
                if (!string.IsNullOrWhiteSpace(collector.Detail)) lines.Add($"    {Loc.Display(collector.Detail)}");
                lines.Add($"    {Loc.T("dg.verify")}: {Loc.Display(collector.Verification)}");
            }
            lines.Add($"  {Loc.T("dg.fieldSources")}");
            lines.AddRange(provider.FieldSourcesText.Split(Environment.NewLine).Select(l => "    " + l));
            lines.Add($"  {L("dg.selected")}{provider.SourceLine}");
            lines.Add("");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildHistoryText()
    {
        var names = State.Providers.Values.ToDictionary(a => a.AccountKey, a => a.AccountName);
        // Newest first, at most 200: walk the history from the end and stop once enough rows are found.
        var history = _store.LoadHistory();
        var cutoff = DateTimeOffset.Now - ChartRange;
        var rows = new List<UsageSnapshot>(200);
        for (var i = history.Count - 1; i >= 0 && rows.Count < 200; i--)
        {
            if (history[i].Timestamp >= cutoff) rows.Add(history[i]);
        }
        var lines = new List<string>
        {
            Loc.T("mv.historyHeader"),
            "----------------------------------------------------------------"
        };
        foreach (var item in rows)
        {
            var account = names.GetValueOrDefault(item.EffectiveKey, item.Account);
            lines.Add($"{item.Timestamp.LocalDateTime:MM-dd HH:mm:ss}  {item.Provider,-8} {account,-10} {item.SessionUsagePercent,3}% {item.WeeklyUsagePercent,5}%  {Loc.Term("src.", item.Source)} / {Loc.Term("conf.", item.Confidence)}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
