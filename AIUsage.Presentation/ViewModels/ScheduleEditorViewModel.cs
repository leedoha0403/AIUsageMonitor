using System.Windows.Input;
using AIUsage.Core;
using AIUsage.Core.Refresh;

namespace AIUsage.Presentation.ViewModels;

public enum ScheduleEditorResult
{
    None,
    Schedule,
    Unschedule
}

// Backs the "Scheduled refresh" dialog. Works on a copy; nothing changes until Schedule is pressed.
public sealed class ScheduleEditorViewModel : ObservableObject
{
    private readonly ProviderViewModel _provider;
    private readonly AccountRefresh _draft;

    public ScheduleEditorViewModel(ProviderViewModel provider, RefreshSettings settings)
    {
        _provider = provider;
        var r = provider.State.Refresh;
        _draft = new AccountRefresh
        {
            Mode = r.Mode, Time = r.Time, DelayMinutes = r.DelayMinutes, Repeat = r.Repeat,
            WindowStart = r.WindowStart, WindowEnd = r.WindowEnd, CostMode = r.CostMode, Model = r.Model,
            PromptMode = r.PromptMode, Prompt = string.IsNullOrWhiteSpace(r.Prompt) ? "hi" : r.Prompt
        };
        var time = RefreshScheduler.ParseTime(_draft.Time);
        _hour = time.Hours;
        _minute = time.Minutes - time.Minutes % 5;
        _windowStartHour = RefreshScheduler.ParseTime(_draft.WindowStart).Hours;
        _windowEndHour = RefreshScheduler.ParseTime(_draft.WindowEnd).Hours;

        ScheduleCommand = new RelayCommand(() => Close(ScheduleEditorResult.Schedule));
        UnscheduleCommand = new RelayCommand(() => Close(ScheduleEditorResult.Unschedule));
        CloseCommand = new RelayCommand(() => Close(ScheduleEditorResult.None));
    }

    public event Action<bool>? RequestClose;
    public ScheduleEditorResult Result { get; private set; }
    public ICommand ScheduleCommand { get; }
    public ICommand UnscheduleCommand { get; }
    public ICommand CloseCommand { get; }

    public string Title => _provider.Title;
    public string NextRenewText => string.IsNullOrEmpty(_provider.NextRenewCountdown)
        ? _provider.NextRenewTime
        : $"{_provider.NextRenewTime}   ({_provider.NextRenewCountdown})";
    public bool IsScheduled => _provider.HasRefreshSchedule;

    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToList();
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 12).Select(i => i * 5).ToList();
    public IReadOnlyList<OptionItem> DelayOptions { get; } = new[] { 15, 30, 60, 90, 120, 180, 240 }
        .Select(n => new OptionItem(n, () => n < 60 ? Loc.T("rf.delay.min", n) : n % 60 == 0 ? Loc.T("rf.delay.hour", n / 60) : Loc.T("rf.delay.hourMin", n / 60, n % 60)))
        .ToList();

    // ---- run time
    public bool ModeAtReset { get => _draft.Mode == "AtReset"; set { if (value) SetMode("AtReset"); } }
    public bool ModeAtTime { get => _draft.Mode == "AtTime"; set { if (value) SetMode("AtTime"); } }
    public bool ModeAfterReset { get => _draft.Mode == "AfterReset"; set { if (value) SetMode("AfterReset"); } }

    private int _hour;
    public int Hour { get => _hour; set { _hour = value; _draft.Time = $"{_hour:00}:{_minute:00}"; Changed(); } }

    private int _minute;
    public int Minute { get => _minute; set { _minute = value; _draft.Time = $"{_hour:00}:{_minute:00}"; Changed(); } }

    public int DelayMinutes { get => _draft.DelayMinutes; set { _draft.DelayMinutes = value; Changed(); } }

    // ---- repeat
    public bool RepeatOnce { get => _draft.Repeat == "Once"; set { if (value) SetRepeat("Once"); } }
    public bool RepeatEvery { get => _draft.Repeat == "Every"; set { if (value) SetRepeat("Every"); } }
    public bool RepeatWindow { get => _draft.Repeat == "Window"; set { if (value) SetRepeat("Window"); } }

    private int _windowStartHour;
    public int WindowStartHour { get => _windowStartHour; set { _windowStartHour = value; _draft.WindowStart = $"{value:00}:00"; Changed(); } }

    private int _windowEndHour;
    public int WindowEndHour { get => _windowEndHour; set { _windowEndHour = value; _draft.WindowEnd = $"{value:00}:00"; Changed(); } }

    // ---- request
    public bool CostMinimal { get => _draft.CostMode == "Minimal"; set { if (value) { _draft.CostMode = "Minimal"; Changed(); } } }
    public bool CostCustom { get => _draft.CostMode == "Custom"; set { if (value) { _draft.CostMode = "Custom"; Changed(); } } }
    public string Model { get => _draft.Model; set { _draft.Model = value ?? ""; Changed(); } }

    public bool PromptDefault { get => _draft.PromptMode == "Default"; set { if (value) { _draft.PromptMode = "Default"; Changed(); } } }
    public bool PromptCustom { get => _draft.PromptMode == "Custom"; set { if (value) { _draft.PromptMode = "Custom"; Changed(); } } }
    public string Prompt { get => _draft.Prompt; set { _draft.Prompt = RefreshRunner.SanitizePrompt(value ?? ""); Changed(); } }

    public string PreviewText
    {
        get
        {
            var planned = RefreshScheduler.Plan(_provider.State, _draft, DateTimeOffset.Now);
            return planned is { } at
                ? Loc.T("rf.dlg.preview", _provider.NextRenewTime, Short(at))
                : Loc.T("rf.dlg.previewNone");
        }
    }

    public void ApplyTo(AccountRefresh target)
    {
        target.Mode = _draft.Mode;
        target.Time = _draft.Time;
        target.DelayMinutes = _draft.DelayMinutes;
        target.Repeat = _draft.Repeat;
        target.WindowStart = _draft.WindowStart;
        target.WindowEnd = _draft.WindowEnd;
        target.CostMode = _draft.CostMode;
        target.Model = _draft.Model.Trim();
        target.PromptMode = _draft.PromptMode;
        target.Prompt = string.IsNullOrWhiteSpace(_draft.Prompt) ? "hi" : _draft.Prompt;
    }

    private void SetMode(string mode)
    {
        _draft.Mode = mode;
        OnPropertyChanged(nameof(ModeAtReset));
        OnPropertyChanged(nameof(ModeAtTime));
        OnPropertyChanged(nameof(ModeAfterReset));
        Changed();
    }

    private void SetRepeat(string repeat)
    {
        _draft.Repeat = repeat;
        OnPropertyChanged(nameof(RepeatOnce));
        OnPropertyChanged(nameof(RepeatEvery));
        OnPropertyChanged(nameof(RepeatWindow));
        Changed();
    }

    private void Changed() => OnPropertyChanged(nameof(PreviewText));

    private void Close(ScheduleEditorResult result)
    {
        Result = result;
        RequestClose?.Invoke(result != ScheduleEditorResult.None);
    }

    private static string Short(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("MM-dd HH:mm");
    }
}
