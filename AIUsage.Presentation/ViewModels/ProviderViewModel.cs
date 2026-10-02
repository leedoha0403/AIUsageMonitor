using System.Collections.ObjectModel;
using System.Windows.Media;
using AIUsage.Core;

namespace AIUsage.Presentation.ViewModels;

public sealed class ProviderViewModel : ObservableObject
{
    private static readonly System.Windows.Media.Color[] SeriesColors =
    [
        System.Windows.Media.Color.FromRgb(31, 138, 112),
        System.Windows.Media.Color.FromRgb(139, 92, 246),
        System.Windows.Media.Color.FromRgb(211, 112, 43),
        System.Windows.Media.Color.FromRgb(37, 99, 235),
        System.Windows.Media.Color.FromRgb(197, 64, 73),
        System.Windows.Media.Color.FromRgb(120, 130, 60)
    ];

    private readonly UsageProviderState _state;
    private string _velocityText = "";
    private string _forecastText = "";
    private string _timelineText = "";
    private string _resetHistoryText = "";
    private bool _isPrimary;
    private bool _cliInstalled = true;
    private bool _isLoginPending;
    private bool _showAccountName;

    public ProviderViewModel(UsageProviderState state, int colorIndex)
    {
        _state = state;
        Collectors = new ObservableCollection<CollectorInfo>(_state.Collectors);
        ModelBreakdown = new ObservableCollection<ModelUsage>(_state.ModelBreakdown);
        var color = SeriesColors[colorIndex % SeriesColors.Length];
        SeriesBrush = new SolidColorBrush(color);
        SeriesBrush.Freeze();
    }

    public UsageProviderState State => _state;
    public ObservableCollection<CollectorInfo> Collectors { get; }
    public ObservableCollection<ModelUsage> ModelBreakdown { get; }
    public SolidColorBrush SeriesBrush { get; }
    public string AccountKey => _state.AccountKey;
    public string ProviderId => _state.ProviderId;
    public string DisplayName => _state.DisplayName;
    public bool IsDefaultAccount => Defaults.ProviderOrder.Contains(_state.AccountKey);
    public bool CanRemove => !IsDefaultAccount;
    public string Title => _showAccountName ? $"{DisplayName} · {AccountName}" : DisplayName;
    public string ShortName => _showAccountName ? $"{DisplayName.Split(' ')[0]}·{AccountName}" : DisplayName.Split(' ')[0];

    public bool IsPrimary
    {
        get => _isPrimary;
        set { if (Set(ref _isPrimary, value)) OnPropertyChanged(nameof(PrimaryMark)); }
    }

    public string PrimaryMark => IsPrimary ? "★" : "";

    public bool ShowAccountName
    {
        get => _showAccountName;
        set
        {
            if (Set(ref _showAccountName, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(ShortName));
                OnPropertyChanged(nameof(ChipUsedText));
                OnPropertyChanged(nameof(ChipRemainingText));
            }
        }
    }

    public bool IsEnabled
    {
        get => _state.Enabled;
        set { _state.Enabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(ListStatus)); }
    }

    public bool ShowInMini
    {
        get => _state.ShowInMini;
        set { _state.ShowInMini = value; OnPropertyChanged(); OnPropertyChanged(nameof(ListStatus)); }
    }

    public bool ShowInDashboard
    {
        get => _state.ShowInDashboard;
        set { _state.ShowInDashboard = value; OnPropertyChanged(); OnPropertyChanged(nameof(ListStatus)); }
    }

    public string ConfigDirectory
    {
        get => _state.ConfigDirectory;
        set { _state.ConfigDirectory = value?.Trim() ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(LoginOptions)); }
    }

    // Copilot accounts pick one of the GitHub logins signed in to the Copilot CLI.
    public bool HasLoginChoice => _state.ProviderId == "copilot";

    public string GitHubLogin
    {
        get => _state.Login;
        // A ComboBox can push null while its items are being replaced; that is not a user choice.
        set { if (value == null) return; _state.Login = value.Trim(); OnPropertyChanged(); }
    }

    public IReadOnlyList<OptionItem> LoginOptions
    {
        get
        {
            var options = new List<OptionItem> { new("", () => Loc.T("ui.acc.githubLoginCurrent")) };
            if (!HasLoginChoice) return options;
            var users = AIUsage.Core.Providers.CopilotLogin.SignedInUsers(_state).Select(u => u.Login).ToList();
            // Keep a saved login selectable even after it was signed out of the CLI.
            if (_state.Login.Length > 0 && !users.Contains(_state.Login, StringComparer.OrdinalIgnoreCase)) users.Add(_state.Login);
            options.AddRange(users.Select(login => new OptionItem(login, () => login)));
            return options;
        }
    }

    public string AccountName
    {
        get => _state.AccountName;
        set
        {
            _state.AccountName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ShortName));
            RefreshDerived();
        }
    }

    public string Plan
    {
        get => _state.Plan;
        set { _state.Plan = value; OnPropertyChanged(); }
    }

    public string Source
    {
        get => _state.Source;
        set { _state.Source = value; OnPropertyChanged(); RefreshDerived(); }
    }

    public string Confidence
    {
        get => _state.Confidence;
        set { _state.Confidence = value; OnPropertyChanged(); RefreshDerived(); }
    }

    // Copilot has no 5H window: its monthly quota (kept in the Weekly* fields) is the primary one.
    public bool HasSessionWindow => _state.Capabilities.SessionUsage;
    public bool IsMonthly => _state.Capabilities.LongWindow == "Monthly";
    // Once the long window is used up, the 5H window no longer matters: nothing can run until the long reset.
    private bool UsesSessionWindow => HasSessionWindow && WeeklyUsagePercent < 100;
    // The separate long-window row only adds something while the 5H window is the primary one; once the long
    // window is used up it already is the primary row.
    public bool ShowWeeklyRow => UsesSessionWindow;
    public int PrimaryPercent => UsesSessionWindow ? SessionUsagePercent : WeeklyUsagePercent;
    public DateTimeOffset PrimaryResetAt => UsesSessionWindow ? _state.SessionResetAt : _state.WeeklyResetAt;
    public string LongWindowLabel => Loc.T(IsMonthly ? "ui.monthly" : "ui.weekly");
    public string PrimaryLabel => UsesSessionWindow ? Loc.T("ui.5h") : LongWindowLabel;
    public string PrimaryLongLabel => UsesSessionWindow ? Loc.T("ui.5hour") : LongWindowLabel;
    public string LongUsageLabel => Loc.T(IsMonthly ? "ui.monthlyUsage" : "ui.weeklyUsage");
    public string LongResetLabel => Loc.T(IsMonthly ? "ui.monthlyReset" : "ui.weeklyReset");

    public int SessionUsagePercent
    {
        get => _state.SessionUsagePercent;
        set { _state.SessionUsagePercent = Math.Clamp(value, 0, 100); RefreshDerived(); }
    }

    public int WeeklyUsagePercent
    {
        get => _state.WeeklyUsagePercent;
        set { _state.WeeklyUsagePercent = Math.Clamp(value, 0, 100); RefreshDerived(); }
    }

    public string SessionResetText
    {
        get => Formatters.LocalTime(_state.SessionResetAt);
        set
        {
            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                _state.SessionResetAt = parsed;
                RefreshDerived();
            }
        }
    }

    public string WeeklyResetText
    {
        get => Formatters.LocalTime(_state.WeeklyResetAt);
        set
        {
            if (DateTimeOffset.TryParse(value, out var parsed))
            {
                _state.WeeklyResetAt = parsed;
                RefreshDerived();
            }
        }
    }

    public bool CliInstalled
    {
        get => _cliInstalled;
        set { if (Set(ref _cliInstalled, value)) RaiseLoginText(); }
    }

    public bool IsLoginPending
    {
        get => _isLoginPending;
        set { if (Set(ref _isLoginPending, value)) RaiseLoginText(); }
    }

    public string LoginButtonText => IsLoginPending ? Loc.T("ui.loginWaiting") : CliInstalled ? Loc.T("ui.login") : Loc.T("ui.installCli");
    public string LoginHint => IsLoginPending ? Loc.T("pv.hint.waiting") : CliInstalled ? Loc.T("pv.hint.login", DisplayName) : Loc.T("pv.hint.install", DisplayName);

    private void RaiseLoginText()
    {
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(LoginHint));
    }

    public string DefaultFolderHint => Loc.T("pv.folderDefault", System.IO.Path.GetDirectoryName(AIUsage.Core.Providers.LoginHelper.CredentialPath(new UsageProviderState
    {
        ProviderId = _state.ProviderId
    })));
    public string ListStatus
    {
        get
        {
            if (!IsEnabled) return Loc.T("pv.off");
            var status = IsSignedOut ? HealthLabel : Loc.T("pv.listStatus", HealthLabel, PrimaryLabel, PrimaryPercent);
            var hidden = new List<string>();
            if (!ShowInMini) hidden.Add(Loc.T("pv.hiddenMini"));
            if (!ShowInDashboard) hidden.Add(Loc.T("pv.hiddenDashboard"));
            return hidden.Count == 0 ? status : $"{status} · {string.Join(", ", hidden)}";
        }
    }
    public string LoginStateText => IsSignedOut ? Loc.T("pv.notSignedIn") : Loc.T("pv.loginOk", PlanLine);

    // ---- Renewal / scheduled refresh display

    // Total attempts allowed (first run + retries), set from the global retry setting.
    public static int RetryMaxForDisplay { get; set; } = 3;

    public AccountRefresh Refresh => _state.Refresh;
    private bool WindowActive => !IsSignedOut && HasSessionWindow && SessionWindow.IsActive(_state, _state.Refresh, DateTimeOffset.Now);
    // With the weekly limit used up no new 5H window can start, however idle the 5H window looks: the next
    // renewal is the weekly reset.
    private bool WeeklyBlocked => !IsSignedOut && HasSessionWindow && WeeklyUsagePercent >= 100 && _state.WeeklyResetAt > DateTimeOffset.Now;
    public string NextRenewTime => IsSignedOut ? "-" : WeeklyBlocked ? ShortTime(_state.WeeklyResetAt) : IsWindowPending ? Loc.T("pv.windowStartingShort") : WindowActive ? ShortTime(_state.SessionResetAt) : Loc.T("rf.renewableNow");
    public string NextRenewCountdown => WeeklyBlocked ? WeeklyCountdown : WindowActive && CountdownIsTime ? Countdown : "";

    public bool NotifyOnReset
    {
        get => _state.Refresh.NotifyOnReset;
        set { _state.Refresh.NotifyOnReset = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotifyButtonText)); }
    }

    public string NotifyButtonText => NotifyOnReset ? Loc.T("rf.notifyOn") : Loc.T("rf.notifyOff");
    public bool HasRefreshSchedule => _state.Refresh.Enabled;
    public bool CanRetryRefresh => _state.Refresh.Status is "Failed" or "Missed";
    public bool IsRefreshRunning => _state.Refresh.Status == "Running";

    public string ScheduleButtonText => _state.Refresh.Enabled && _state.Refresh.ScheduledFor is { } at && _state.Refresh.Status is "Scheduled" or "Waiting"
        ? $"⏱ {ShortTime(at)}"
        : Loc.T("rf.scheduleButton");

    public string RefreshStatusText
    {
        get
        {
            var r = _state.Refresh;
            var now = DateTimeOffset.Now;
            switch (r.Status)
            {
                case "Scheduled" when r.ScheduledFor is { } at:
                    return at - now <= TimeSpan.FromMinutes(10) && at > now
                        ? Loc.T("rf.status.Imminent", (at - now).ToString(@"hh\:mm\:ss"))
                        : Loc.T("rf.status.Scheduled", ShortTime(at));
                case "Waiting" when r.ScheduledFor is { } at:
                    return Loc.T("rf.status.Waiting", ShortTime(at));
                case "Running":
                    return Loc.T("rf.status.Running");
                case "Success" when r.LastSuccessAt is { } ok:
                    return Loc.T("rf.status.Success", ShortTime(ok));
                case "Failed":
                    return Loc.T("rf.status.Failed", Loc.T("rf.err." + (string.IsNullOrEmpty(r.LastErrorKind) ? "Other" : r.LastErrorKind)));
                case "RetryWaiting" when r.NextRetryAt is { } retry:
                    return Loc.T("rf.status.RetryWaiting", ShortTime(retry), Math.Max(1, r.Attempts), RetryMaxForDisplay + 1);
                case "Missed":
                    return Loc.T("rf.status.Missed");
                case "Blocked":
                    return Loc.T("rf.status.Blocked");
                default:
                    return Loc.T("rf.status.Idle");
            }
        }
    }

    public string ScheduleSummary
    {
        get
        {
            var r = _state.Refresh;
            var mode = r.CostMode == "Custom" && !string.IsNullOrWhiteSpace(r.Model) ? Loc.T("rf.mode.custom", r.Model) : Loc.T("rf.mode.minimal");
            var repeat = r.Repeat == "Window" ? Loc.T("rf.repeat.Window", r.WindowStart, r.WindowEnd) : Loc.T("rf.repeat." + r.Repeat);
            return $"{mode} · {repeat}";
        }
    }

    // "HH:mm", with the date when it is not today.
    private static string ShortTime(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("MM-dd HH:mm");
    }

    public string Status => _state.Status;
    public string Message => Loc.Display(_state.Message);
    public bool IsSignedOut => _state.Status == "NOT_SIGNED_IN";
    public bool HasUsage => !IsSignedOut;
    // Renewal/scheduled refresh is about the 5H window only.
    public bool HasRenewal => HasUsage && HasSessionWindow;
    public string UsedLine => IsSignedOut ? Loc.T("pv.notSignedIn") : Loc.T("pv.used", PrimaryPercent);
    public string RemainingLine => IsSignedOut ? Loc.T("pv.notSignedIn") : Loc.T("pv.left", 100 - PrimaryPercent);
    // A window we just started via scheduled refresh, whose new reset time the usage endpoint hasn't reported yet
    // (it lags the request by a little). Until then the stored reset is the old, already passed one.
    public bool IsWindowPending => HasSessionWindow && !IsSignedOut && WindowActive && _state.SessionResetAt <= DateTimeOffset.Now;
    // Countdown is a real time span, not a state such as "renewable now".
    // Signed out means we cannot confirm the window is still active, so the countdown must not keep ticking
    // toward a possibly-stale reset time - show a static placeholder instead (see NextRenewTime's "-").
    private bool CountdownIsTime => !IsSignedOut && (!UsesSessionWindow || (WindowActive && !IsWindowPending)) && PrimaryResetAt > DateTimeOffset.Now;
    public string Countdown => IsSignedOut ? "-"
        : !UsesSessionWindow || (WindowActive && !IsWindowPending) ? Formatters.Countdown(PrimaryResetAt)
        : IsWindowPending ? Loc.T("pv.windowStarting") : Loc.T("rf.renewableNow");
    public string WeeklyCountdown => IsSignedOut ? "-" : Formatters.Countdown(_state.WeeklyResetAt);
    // Small line under the 5H countdown, so the long window's reset is visible without a second card.
    public string WeeklyResetCountdownLine => Loc.T(IsMonthly ? "pv.monthlyResetLine" : "pv.weeklyResetLine", WeeklyCountdown);
    public string ResetState => CountdownIsTime ? Formatters.ResetState(PrimaryResetAt) : "";
    public string WeeklyResetLine => Formatters.LocalTime(_state.WeeklyResetAt);
    public string UsageState => Formatters.UsageState(PrimaryPercent);
    public string SourceLine => $"{Loc.Term("src.", Source)} / {Loc.Term("conf.", Confidence)}";
    public string SourceDisplayLine => Loc.T("pv.sourceLine", SourceLine);
    public string WeeklyResetDisplay => Loc.T(IsMonthly ? "pv.monthlyResetLine" : "pv.weeklyResetLine", WeeklyResetLine);
    public string CountdownLine => CountdownIsTime ? Loc.T("pv.resetIn", Formatters.DockCountdown(PrimaryResetAt)) : Countdown;
    public string ChipCountdown => IsSignedOut ? "-"
        : !UsesSessionWindow || (WindowActive && !IsWindowPending) ? Formatters.ShortCountdown(PrimaryResetAt)
        : IsWindowPending ? Loc.T("pv.windowStartingShort") : Loc.T("rf.renewableShort");
    public string WeeklyUsedLine => Loc.T(IsMonthly ? "pv.monthUsed" : "pv.weekUsed", WeeklyUsagePercent, WeeklyCountdown);
    public string WeeklyRemainingLine => Loc.T(IsMonthly ? "pv.monthLeft" : "pv.weekLeft", 100 - WeeklyUsagePercent, WeeklyCountdown);
    public string ChipUsedText => IsSignedOut ? $"{ShortName} –" : $"{ShortName} {PrimaryPercent}%";
    public string ChipRemainingText => IsSignedOut ? $"{ShortName} –" : $"{ShortName} {100 - PrimaryPercent}%";
    public string Summary => IsSignedOut ? Message : Loc.T(CountdownIsTime ? "pv.summary" : "pv.summaryState", 100 - PrimaryPercent, Countdown);
    public string PlanLine => Loc.T("pv.plan", _state.Plan);
    public string LastSuccessText => Loc.T("pv.lastSuccess", Formatters.Ago(_state.LastSuccessAt));
    public bool HasModelBreakdown => ModelBreakdown.Count > 0;
    public bool HasExtraUsage => _state.ExtraUsage is { IsEnabled: true } || !string.IsNullOrEmpty(_state.CreditsBalance);

    public string ExtraUsageText
    {
        get
        {
            if (_state.ExtraUsage is { IsEnabled: true } extra)
            {
                var used = extra.UsedDollars.HasValue ? $"${extra.UsedDollars.Value:0.00}" : "-";
                var limit = extra.MonthlyLimitDollars.HasValue ? $" / ${extra.MonthlyLimitDollars.Value:0.00}" : "";
                return Loc.T("pv.extra", used, limit);
            }
            if (string.IsNullOrEmpty(_state.CreditsBalance)) return "";
            // Copilot stores a full message (premium-request overage), not a balance.
            return IsMonthly ? Loc.Display(_state.CreditsBalance) : Loc.T("pv.credits", _state.CreditsBalance);
        }
    }

    public string HealthText
    {
        get
        {
            var selected = _state.Collectors.FirstOrDefault(c => c.Status == "SUCCESS" && c.Name != "Local") ?? _state.Collectors.FirstOrDefault(c => c.Status == "SUCCESS");
            var latency = selected?.LatencyMs is { } ms ? $" · {ms} ms" : "";
            var failures = _state.ConsecutiveFailures > 0 ? Loc.T("pv.failures", _state.ConsecutiveFailures) : "";
            var via = selected == null ? Loc.T("pv.none") : Loc.Term("src.", selected.Name);
            return Loc.T("pv.health", HealthLabel, via, latency, failures);
        }
    }

    public string HealthLabel => Loc.Term("health.", _state.Status);

    public System.Windows.Media.Brush HealthBrush => _state.Status switch
    {
        "READY" => Freeze(System.Windows.Media.Color.FromRgb(31, 138, 112)),
        "LOCAL" => Freeze(System.Windows.Media.Color.FromRgb(120, 130, 140)),
        "NOT_SIGNED_IN" => Freeze(System.Windows.Media.Color.FromRgb(140, 146, 152)),
        _ => Freeze(System.Windows.Media.Color.FromRgb(211, 112, 43))
    };

    public string FieldSourcesText => string.Join(Environment.NewLine, new[]
    {
        (Loc.T("pv.field.session"), "sessionUsagePercent"), (Loc.T("pv.field.sessionReset"), "sessionResetAt"),
        (Loc.T(IsMonthly ? "pv.field.monthly" : "pv.field.weekly"), "weeklyUsagePercent"),
        (Loc.T(IsMonthly ? "pv.field.monthlyReset" : "pv.field.weeklyReset"), "weeklyResetAt"), (Loc.T("pv.field.plan"), "plan")
    }.Where(f => HasSessionWindow || !f.Item2.StartsWith("session")).Select(f =>
    {
        var fs = _state.FieldSources.GetValueOrDefault(f.Item2);
        var label = f.Item1.PadRight(Loc.IsKorean ? 8 : 13);
        return fs == null ? $"{label} -" : $"{label} {Loc.Term("src.", fs.Source)} · {Loc.Term("conf.", fs.Confidence)}{(fs.UpdatedAt.HasValue ? $" · {fs.UpdatedAt.Value.ToLocalTime():HH:mm:ss}" : "")}";
    }));

    public string VelocityText
    {
        get => _velocityText;
        private set => Set(ref _velocityText, value);
    }

    public string ForecastText
    {
        get => _forecastText;
        private set => Set(ref _forecastText, value);
    }

    public string TimelineText
    {
        get => _timelineText;
        private set => Set(ref _timelineText, value);
    }

    public string ResetHistoryText
    {
        get => _resetHistoryText;
        private set => Set(ref _resetHistoryText, value);
    }

    public System.Windows.Media.Brush AccentBrush => IsSignedOut ? Freeze(System.Windows.Media.Color.FromRgb(140, 146, 152)) : AccentFor(UsageState);

    // The long window's own colour, so its bar never contradicts the percentage next to it.
    public System.Windows.Media.Brush WeeklyAccentBrush => IsSignedOut ? Freeze(System.Windows.Media.Color.FromRgb(140, 146, 152)) : AccentFor(Formatters.UsageState(WeeklyUsagePercent));

    private static System.Windows.Media.Brush AccentFor(string usageState) => usageState switch
    {
        "Critical" => Freeze(System.Windows.Media.Color.FromRgb(197, 64, 73)),
        "High" => Freeze(System.Windows.Media.Color.FromRgb(211, 112, 43)),
        "Notice" => Freeze(System.Windows.Media.Color.FromRgb(195, 142, 36)),
        _ => Freeze(System.Windows.Media.Color.FromRgb(31, 138, 112))
    };

    public void UpdateAnalytics(IReadOnlyList<UsageSnapshot> history)
    {
        if (IsSignedOut)
        {
            VelocityText = ForecastText = "-";
            TimelineText = ResetHistoryText = Loc.T("pv.signedOutData");
            return;
        }
        // Velocity/forecast/reset history are modeled on the 5H window; a monthly quota has none of that.
        if (!HasSessionWindow)
        {
            VelocityText = "-";
            ForecastText = TimelineText = ResetHistoryText = Loc.T("pv.noForecastMonthly");
            return;
        }
        // The long window is used up: nothing can run until its reset, whatever the 5H window says.
        if (!UsesSessionWindow)
        {
            var left = _state.WeeklyResetAt - DateTimeOffset.Now;
            VelocityText = "-";
            ForecastText = left > TimeSpan.Zero ? Loc.T("an.limitReached", Formatters.Duration(left)) : Loc.T("an.limitReachedPending");
            TimelineText = ResetHistoryText = "-";
            return;
        }
        var window = UsageAnalytics.CurrentWindow(history, _state);
        var velocity = UsageAnalytics.Velocity(window, _state);
        VelocityText = velocity == null
            ? Loc.T("pv.noHistory")
            : Loc.T("pv.velocity", (velocity.Last30Minutes >= 0 ? "+" : "") + velocity.Last30Minutes, velocity.PerHour);
        ForecastText = UsageAnalytics.Forecast(velocity, _state);
        TimelineText = string.Join(Environment.NewLine, UsageAnalytics.Timeline(window, _state));
        var resets = UsageAnalytics.ResetHistory(history, _state.AccountKey, TimeSpan.FromDays(7));
        ResetHistoryText = resets.Count == 0
            ? Loc.T("pv.noResets")
            : Loc.T("pv.resetSummary", resets.Count, resets.Count(r => r.LimitReached)) + Environment.NewLine +
              string.Join(Environment.NewLine, resets.Take(6).Select(r => $"{r.ResetAt.ToLocalTime():MM-dd HH:mm}  {Loc.T("pv.peak")} {r.Peak,3}%{(r.LimitReached ? "  " + Loc.T("an.limit") : "")}"));
    }

    public void TouchManualSources()
    {
        _state.CollectedAt = DateTimeOffset.Now;
        _state.Status = "READY";
        _state.Message = Loc.Msg("msg.manualSaved");
        foreach (var field in new[] { "sessionUsagePercent", "sessionResetAt", "weeklyUsagePercent", "weeklyResetAt", "plan" })
        {
            _state.FieldSources[field] = new FieldSource { Source = Source, Confidence = Confidence, UpdatedAt = DateTimeOffset.Now };
        }
        RefreshDerived();
        RefreshCollectors();
    }

    public void RefreshDerived()
    {
        foreach (var name in new[]
                 {
                     nameof(Status), nameof(Message), nameof(IsSignedOut), nameof(HasUsage), nameof(UsedLine), nameof(RemainingLine),
                     nameof(Countdown), nameof(WeeklyCountdown), nameof(WeeklyResetCountdownLine), nameof(ResetState), nameof(WeeklyResetLine), nameof(UsageState),
                     nameof(SourceLine), nameof(WeeklyUsedLine), nameof(WeeklyRemainingLine), nameof(ChipUsedText), nameof(ChipRemainingText),
                     nameof(Summary), nameof(AccentBrush), nameof(SessionUsagePercent), nameof(WeeklyUsagePercent), nameof(SessionResetText),
                     nameof(WeeklyResetText), nameof(Plan), nameof(PlanLine), nameof(LastSuccessText), nameof(HealthText), nameof(HealthLabel),
                     nameof(HealthBrush), nameof(Source), nameof(Confidence), nameof(SourceDisplayLine), nameof(WeeklyResetDisplay),
                     nameof(CountdownLine), nameof(ChipCountdown), nameof(FieldSourcesText), nameof(ExtraUsageText),
                     nameof(LoginButtonText), nameof(LoginHint), nameof(ListStatus), nameof(LoginStateText), nameof(DefaultFolderHint),
                     nameof(NextRenewTime), nameof(NextRenewCountdown), nameof(HasRefreshSchedule), nameof(CanRetryRefresh), nameof(IsRefreshRunning),
                     nameof(ScheduleButtonText), nameof(RefreshStatusText), nameof(ScheduleSummary), nameof(NotifyButtonText), nameof(NotifyOnReset),
                     nameof(PrimaryPercent), nameof(PrimaryResetAt), nameof(PrimaryLabel), nameof(PrimaryLongLabel), nameof(LongWindowLabel),
                     nameof(LongUsageLabel), nameof(LongResetLabel), nameof(HasSessionWindow), nameof(IsMonthly), nameof(HasRenewal), nameof(IsWindowPending),
                     nameof(ShowWeeklyRow), nameof(WeeklyAccentBrush)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    // Properties whose value changes with the clock alone (they read "now", directly or through WindowActive /
    // IsWindowPending / CountdownIsTime). The once-a-second tick raises just these; every other property only
    // changes when the state does, which already goes through RefreshDerived.
    private static readonly string[] TimeDependent =
    [
        nameof(Countdown), nameof(WeeklyCountdown), nameof(WeeklyResetCountdownLine), nameof(ResetState), nameof(CountdownLine), nameof(ChipCountdown),
        nameof(WeeklyUsedLine), nameof(WeeklyRemainingLine), nameof(Summary), nameof(LastSuccessText), nameof(RefreshStatusText),
        nameof(NextRenewTime), nameof(NextRenewCountdown), nameof(IsWindowPending)
    ];

    public static IReadOnlyList<string> TimeDependentProperties => TimeDependent;

    public void RefreshTime()
    {
        foreach (var name in TimeDependent) OnPropertyChanged(name);
    }

    public void RefreshCollectors()
    {
        Collectors.Clear();
        foreach (var collector in _state.Collectors) Collectors.Add(collector);
        ModelBreakdown.Clear();
        foreach (var model in _state.ModelBreakdown) ModelBreakdown.Add(model);
        OnPropertyChanged(nameof(HasModelBreakdown));
        OnPropertyChanged(nameof(HasExtraUsage));
        OnPropertyChanged(nameof(ExtraUsageText));
        OnPropertyChanged(nameof(FieldSourcesText));
    }

    private static SolidColorBrush Freeze(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
