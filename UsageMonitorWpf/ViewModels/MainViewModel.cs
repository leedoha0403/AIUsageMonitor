using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using UsageMonitorWpf.Controls;
using UsageMonitorWpf.Core;
using UsageMonitorWpf.Providers;
using UsageMonitorWpf.Refresh;
using UsageMonitorWpf.Storage;

namespace UsageMonitorWpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Dictionary<string, TimeSpan> RangeSpans = new()
    {
        ["1H"] = TimeSpan.FromHours(1),
        ["6H"] = TimeSpan.FromHours(6),
        ["1D"] = TimeSpan.FromDays(1),
        ["7D"] = TimeSpan.FromDays(7),
        ["30D"] = TimeSpan.FromDays(30)
    };

    private readonly StateStore _store;
    private readonly UsageAggregator _aggregator = new();
    private readonly DispatcherTimer _countdownTimer = new();
    private readonly DispatcherTimer _refreshTimer = new();
    private string _diagnosticsText = "";
    private string _historyText = "";
    private string _language;
    private string _widgetMode;
    private string _windowVersion;
    private string _theme;
    private string _displayUsageAs;
    private string _collectionLevel;
    private int _refreshSeconds;
    private bool _alwaysOnTop;
    private string _historyRange;
    private string _thresholdsText;
    private IReadOnlyList<ChartSeries> _chartSeries = [];
    private bool _isRefreshing;
    private int _tick;
    private ProviderViewModel? _selectedAccount;
    private int _selectedTabIndex;

    public MainViewModel(StateStore store)
    {
        _store = store;
        State = _store.LoadState();
        Providers = new ObservableCollection<ProviderViewModel>();
        ActiveProviders = new ObservableCollection<ProviderViewModel>();
        MiniProviders = new ObservableCollection<ProviderViewModel>();
        DashboardProviders = new ObservableCollection<ProviderViewModel>();
        _language = State.Settings.Language;
        _widgetMode = State.Settings.WidgetMode;
        _windowVersion = State.Settings.WindowVersion;
        _theme = State.Settings.Theme;
        _displayUsageAs = State.Settings.DisplayUsageAs;
        _collectionLevel = State.Settings.CollectionLevel;
        _refreshSeconds = State.Settings.RefreshSeconds;
        _alwaysOnTop = State.Settings.AlwaysOnTop;
        _historyRange = RangeSpans.ContainsKey(State.Settings.HistoryRange) ? State.Settings.HistoryRange : "1D";
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
            (title, body) => NotificationRequested?.Invoke(title, body, null),
            OnRefreshStateChanged);
        CancelScheduleCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) { _scheduler.Cancel(vm.State); } });
        RetryRefreshCommand = new ParamCommand(p => { if (p is ProviderViewModel vm) { _scheduler.Retry(vm.State); _ = _scheduler.TickAsync(DateTimeOffset.Now); } });
        ProviderViewModel.RetryMaxForDisplay = State.Settings.Refresh.RetryMax;
        _schedulerTimer.Tick += (_, _) => _ = _scheduler.TickAsync(DateTimeOffset.Now);
        _schedulerTimer.Start();
        // First check shortly after launch instead of waiting a full interval.
        var firstTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        firstTick.Tick += (_, _) => { firstTick.Stop(); _ = _scheduler.TickAsync(DateTimeOffset.Now); };
        firstTick.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Loc.Apply(_language);
        ThemeService.Apply(Theme);

        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += (_, _) =>
        {
            RefreshDerived();
            if (++_tick % 30 == 0) RefreshAnalytics();
        };
        _countdownTimer.Start();
        ConfigureRefreshTimer();
        RefreshTextViews();
    }

    private readonly RefreshRunner _runner;
    private readonly RefreshScheduler _scheduler;
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
        var window = new RefreshScheduleWindow(editor) { Owner = System.Windows.Application.Current.MainWindow?.IsVisible == true ? System.Windows.Application.Current.MainWindow : null };
        if (window.ShowDialog() != true) return;
        if (editor.Result == ScheduleEditorResult.Schedule)
        {
            editor.ApplyTo(provider.State.Refresh);
            _scheduler.Arm(provider.State);
            _ = _scheduler.TickAsync(DateTimeOffset.Now);
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
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            await _scheduler.TickAsync(DateTimeOffset.Now);
        });
    }

    // title, body, optional action when the notification is clicked
    public event Action<string, string, Action?>? NotificationRequested;
    private readonly HashSet<string> _nudged = new();
    // Asks the window layer to bring up the widget, where the login button lives.
    public event Action? LoginPromptRequested;
    private readonly Dictionary<string, (bool Install, DateTimeOffset Baseline, DateTimeOffset Until)> _loginWatches = new();
    private readonly DispatcherTimer _loginTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public AppState State { get; private set; }
    public ObservableCollection<ProviderViewModel> Providers { get; }
    // Collected accounts (monitoring on), and the subsets shown in the mini widget/chips and the dashboard.
    public ObservableCollection<ProviderViewModel> ActiveProviders { get; }
    public ObservableCollection<ProviderViewModel> MiniProviders { get; }
    public ObservableCollection<ProviderViewModel> DashboardProviders { get; }
    public IReadOnlyList<OptionItem> Languages { get; } = Options("Korean", "English");
    public IReadOnlyList<OptionItem> WidgetModes { get; } = Options("Compact", "Normal", "Detailed");
    public IReadOnlyList<OptionItem> WindowVersions { get; } = Options("Mini", "Expanded");
    public IReadOnlyList<OptionItem> Themes { get; } = Options("System", "Light", "Dark");
    public IReadOnlyList<OptionItem> DisplayOptions { get; } = Options("Remaining", "Used");
    public IReadOnlyList<OptionItem> CollectionLevels { get; } = Options(CollectorPolicy.Levels);
    public IReadOnlyList<OptionItem> HistoryRanges { get; } = Options(RangeSpans.Keys.ToArray());
    public IReadOnlyList<OptionItem> ProviderIds { get; } = Options(Defaults.ProviderOrder);
    public IReadOnlyList<OptionItem> RefreshIntervals { get; } = new[] { 0, 15, 30, 60, 120, 300, 900 }
        .Select(n => new OptionItem(n, () => n == 0 ? Loc.T("opt.refresh.0") : n < 60 ? Loc.T("opt.refresh.sec", n) : Loc.T("opt.refresh.min", n / 60)))
        .ToList();
    public IReadOnlyList<OptionItem> OnOffOptions { get; } = [new OptionItem("On", () => Loc.T("ui.on")), new OptionItem("Off", () => Loc.T("ui.off"))];
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
    public string InternalVersion { get; } = typeof(MainViewModel).Assembly
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

    public string WidgetMode
    {
        get => _widgetMode;
        set { if (value != null && Set(ref _widgetMode, value)) { State.Settings.WidgetMode = value; SaveStateOnly(); OnPropertyChanged(nameof(IsCompact)); OnPropertyChanged(nameof(IsDetailed)); } }
    }

    public string WindowVersion
    {
        get => _windowVersion;
        set
        {
            if (value != null && Set(ref _windowVersion, value))
            {
                State.Settings.WindowVersion = value;
                SaveStateOnly();
                OnPropertyChanged(nameof(IsMiniVersion));
                OnPropertyChanged(nameof(IsExpandedVersion));
            }
        }
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (value != null && Set(ref _theme, value))
            {
                State.Settings.Theme = value;
                ThemeService.Apply(value);
                SaveStateOnly();
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

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set { if (Set(ref _alwaysOnTop, value)) { State.Settings.AlwaysOnTop = value; SaveStateOnly(); } }
    }

    public double WidgetOpacity
    {
        get => State.Settings.WidgetOpacity;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.2, 1.0), 2);
            if (Math.Abs(State.Settings.WidgetOpacity - clamped) < 0.001) return;
            State.Settings.WidgetOpacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WidgetOpacityText));
            SaveStateOnly();
        }
    }

    public string WidgetOpacityText => $"{WidgetOpacity * 100:0}%";

    public bool HoverOpaque
    {
        get => State.Settings.HoverOpaque;
        set { State.Settings.HoverOpaque = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public event Action? LanguageChanged;

    public bool RunAtStartup
    {
        get => StartupService.IsEnabled;
        set
        {
            try
            {
                StartupService.SetEnabled(value);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(Loc.T("mv.startupFailed", ex.Message), Loc.T("ui.appName"));
            }
            OnPropertyChanged();
        }
    }

    public bool NotificationsEnabled
    {
        get => State.Settings.NotificationsEnabled;
        set { State.Settings.NotificationsEnabled = value; OnPropertyChanged(); SaveStateOnly(); }
    }

    public bool ShowTaskbarChips
    {
        get => State.Settings.ShowTaskbarChips;
        set { State.Settings.ShowTaskbarChips = value; OnPropertyChanged(); SaveStateOnly(); }
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

    public IReadOnlyList<ChartSeries> ChartSeries
    {
        get => _chartSeries;
        private set => Set(ref _chartSeries, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => Set(ref _isRefreshing, value);
    }

    public bool IsCompact => WidgetMode == "Compact";
    public bool IsDetailed => WidgetMode == "Detailed";
    public bool IsMiniVersion => WindowVersion == "Mini";
    public bool IsExpandedVersion => WindowVersion == "Expanded";
    public bool ShowRemaining => DisplayUsageAs == "Remaining";
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

    public async Task RefreshAsync(bool force = false)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            await _aggregator.RefreshAsync(State, force);
            _store.SaveState(State);
            _store.AppendHistory(State);
            foreach (var provider in Providers) provider.RefreshCollectors();
            RefreshDerived();
            RefreshTextViews();
            CheckThresholds();
            UpdateLoginState();
            NudgeSignedOut();
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
        foreach (var provider in Providers) provider.TouchManualSources();
        _store.SaveState(State);
        _store.AppendHistory(State, force: true);
        RefreshDerived();
        RefreshTextViews();
        CheckThresholds();
    }

    public void SaveWindowPlacement(double left, double top)
    {
        State.WindowLeft = left;
        State.WindowTop = top;
        SaveStateOnly();
    }

    public void SaveWidgetPlacement(double left, double top, string dockEdge, bool folded)
    {
        State.WidgetLeft = left;
        State.WidgetTop = top;
        State.WidgetDockEdge = dockEdge;
        State.WidgetFolded = folded;
        SaveStateOnly();
    }

    public void SaveChipsPlacement(double left, double top)
    {
        State.ChipsLeft = left;
        State.ChipsTop = top;
        SaveStateOnly();
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
        var answer = System.Windows.MessageBox.Show(Loc.T("mv.removeBody", account.Title), Loc.T("mv.removeTitle"),
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.OK) return;
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

    private static void BrowseFolder(ProviderViewModel account)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Loc.T("ui.acc.folder"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = string.IsNullOrWhiteSpace(account.ConfigDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : Environment.ExpandEnvironmentVariables(account.ConfigDirectory)
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) account.ConfigDirectory = dialog.SelectedPath;
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
        OnPropertyChanged(nameof(StatusSummary));
    }

    // Keeps the collected/mini/dashboard lists in step with each account's switches. Items are moved in place
    // (same view model instances), so toggling back and forth never rebinds the account being edited.
    private void SyncVisibleCollections()
    {
        Sync(ActiveProviders, Providers.Where(p => p.IsEnabled));
        Sync(MiniProviders, Providers.Where(p => p.IsEnabled && p.ShowInMini));
        Sync(DashboardProviders, Providers.Where(p => p.IsEnabled && p.ShowInDashboard));
        BuildHistoryChart();
        OnPropertyChanged(nameof(StatusSummary));
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

    // Notifies once per 5H window for each newly crossed threshold (the highest one crossed).
    private void CheckThresholds()
    {
        if (!State.Settings.NotificationsEnabled) return;
        var changed = false;
        foreach (var provider in ActiveProviders.Where(p => p.ShowInMini || p.ShowInDashboard))
        {
            var account = provider.State;
            if (provider.IsSignedOut) continue;
            if (account.NotifiedWindowResetAt is not { } notified || (notified - account.SessionResetAt).Duration() > TimeSpan.FromMinutes(10))
            {
                account.NotifiedWindowResetAt = account.SessionResetAt;
                account.LastNotifiedThreshold = 0;
                changed = true;
            }
            var crossed = State.Settings.NotificationThresholds.Where(t => account.SessionUsagePercent >= t).DefaultIfEmpty(0).Max();
            if (crossed > account.LastNotifiedThreshold)
            {
                account.LastNotifiedThreshold = crossed;
                changed = true;
                NotificationRequested?.Invoke(
                    Loc.T("mv.notifyTitle", provider.Title, account.SessionUsagePercent),
                    Loc.T("mv.notifyBody", crossed, provider.Countdown, provider.SourceLine),
                    null);
            }
        }
        if (changed) SaveStateOnly();
    }

    // ---- Login guidance: open the CLI login (or installer) and wait for the credential file to appear.

    public void StartLogin(ProviderViewModel provider)
    {
        var account = provider.State;
        if (LoginHelper.FindCli(account.ProviderId) != null)
        {
            if (!LoginHelper.LaunchLogin(account)) return;
            Watch(provider, install: false);
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            Loc.T("mv.installBody", provider.DisplayName, LoginHelper.InstallCommand(account.ProviderId)),
            Loc.T("mv.installTitle", provider.DisplayName),
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.OK) return;
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
                NotificationRequested?.Invoke(Loc.T("mv.installDetectedTitle", provider.DisplayName), Loc.T("mv.installDetectedBody"), null);
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
            NotificationRequested?.Invoke(Loc.T("mv.loginDetectedTitle", provider.Title), Loc.T("mv.loginDetectedBody"), null);
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
            NotificationRequested?.Invoke(
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

    private void SaveStateOnly() => _store.SaveState(State);

    private void RefreshDerived()
    {
        foreach (var provider in Providers) provider.RefreshDerived();
        OnPropertyChanged(nameof(StatusSummary));
    }

    private void RefreshAnalytics()
    {
        var history = _store.LoadHistory();
        foreach (var provider in Providers) provider.UpdateAnalytics(history);
    }

    private void RefreshTextViews()
    {
        DiagnosticsText = BuildDiagnosticsText();
        HistoryText = BuildHistoryText();
        RefreshAnalytics();
        BuildHistoryChart();
    }

    private void BuildHistoryChart()
    {
        var history = _store.LoadHistory();
        var since = DateTimeOffset.Now - ChartRange - ChartRange;
        ChartSeries = DashboardProviders.Select(p => new ChartSeries
        {
            Name = p.Title,
            Brush = p.SeriesBrush,
            Points = history.Where(x => x.EffectiveKey == p.AccountKey && x.Timestamp >= since)
                .Select(x => (x.Timestamp, (double)x.SessionUsagePercent))
                .ToList()
        }).ToList();
    }

    private void OnLanguageChanged()
    {
        Loc.Apply(Language);
        foreach (var option in new[] { Languages, WidgetModes, WindowVersions, Themes, DisplayOptions, CollectionLevels, HistoryRanges, ProviderIds, RefreshIntervals, OnOffOptions }.SelectMany(x => x))
        {
            option.Refresh();
        }
        foreach (var option in PreNotifyOptions.Concat(RetryIntervalOptions).Concat(RetryMaxOptions)) option.Refresh();
        foreach (var name in new[] { nameof(AppTitle), nameof(AppSubtitle), nameof(FetchWarning), nameof(DisplayUsageLabel), nameof(CollectionLevelLabel), nameof(RefreshLogText) })
        {
            OnPropertyChanged(name);
        }
        RefreshDerived();
        RefreshTextViews();
        LanguageChanged?.Invoke();
    }

    private static List<OptionItem> Options(params string[] values) =>
        values.Select(v => new OptionItem(v, () => Loc.Term("opt.", v))).ToList();

    private string BuildDiagnosticsText()
    {
        string L(string key) => Loc.T(key).PadRight(Loc.IsKorean ? 10 : 14);
        var lines = new List<string>
        {
            $"{Loc.T("ui.appName")} {InternalVersion}",
            $"{L("dg.runtime")}.NET {RuntimeVersion}",
            $"{L("dg.schema")}{StateSchemaVersion}",
            $"{L("dg.mode")}{Loc.Term("opt.", WindowVersion)} / {Loc.Term("opt.", WidgetMode)}",
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
        var rows = _store.LoadHistory().Where(x => x.Timestamp >= DateTimeOffset.Now - ChartRange).TakeLast(200).Reverse().ToList();
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
