using UsageMonitorWpf.Core;

namespace UsageMonitorWpf.Refresh;

// Decides when each account's scheduled refresh runs and drives RefreshRunner.
// Rules:
// - never run while the current 5H window is still active (the planned time moves to the window's reset);
// - one run per account and planned time (Service + RefreshWindow key); Running/Success block repeats;
// - failures retry a bounded number of times;
// - a time missed while the PC slept follows the MissedPolicy.
public sealed class RefreshScheduler
{
    private static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ResetTolerance = TimeSpan.FromSeconds(30);

    private readonly AppState _state;
    private readonly RefreshRunner _runner;
    private readonly Func<bool, Task> _refreshUsage;
    private readonly Action<string, string> _notify;
    private readonly Action _changed;
    private readonly HashSet<string> _running = new();
    private bool _ticking;

    public RefreshScheduler(AppState state, RefreshRunner runner, Func<bool, Task> refreshUsage, Action<string, string> notify, Action changed)
    {
        _state = state;
        _runner = runner;
        _refreshUsage = refreshUsage;
        _notify = notify;
        _changed = changed;
    }

    // When the refresh would run for the current settings, or null if it cannot be planned (signed out).
    public static DateTimeOffset? Plan(UsageProviderState account, AccountRefresh r, DateTimeOffset now)
    {
        if (account.Status == "NOT_SIGNED_IN") return null;
        var reset = account.SessionResetAt;
        var basis = reset > now ? reset : now;
        DateTimeOffset target = r.Mode switch
        {
            "AtTime" => NextTimeOfDay(r.Time, now),
            "AfterReset" => Max(reset.AddMinutes(Math.Max(0, r.DelayMinutes)), now),
            _ => basis
        };
        // Safety: never earlier than the moment a new window can start.
        var effective = Max(target, reset);
        if (r.Repeat == "Window") effective = IntoWindow(effective, r.WindowStart, r.WindowEnd);
        return TruncateToSecond(effective);
    }

    public async Task TickAsync(DateTimeOffset now)
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            var changed = false;
            foreach (var account in _state.Providers.Values.Where(a => a.Enabled).ToList())
            {
                changed |= HandleNotifications(account, now);
                var r = account.Refresh;
                if (!r.Enabled || _running.Contains(account.AccountKey)) continue;

                if (account.Status == "NOT_SIGNED_IN")
                {
                    if (r.Status != "Blocked") { r.Status = "Blocked"; changed = true; }
                    continue;
                }

                switch (r.Status)
                {
                    case "Idle":
                    case "Blocked":
                        changed |= Schedule(account, now);
                        // Already due (e.g. "as soon as renewable" while renewable): run in this pass.
                        if (r.Status == "Scheduled" && r.ScheduledFor is { } planned && now >= planned) await ExecuteAsync(account, now, isRetry: false);
                        continue;
                    case "Success":
                    case "Failed":
                    case "Missed":
                        // Repeating schedules plan again once a new window is known.
                        if (r.Repeat != "Once" && account.SessionResetAt != r.PlannedForReset) changed |= Schedule(account, now);
                        continue;
                    case "RetryWaiting":
                        if (r.NextRetryAt is { } retryAt && now >= retryAt) await ExecuteAsync(account, now, isRetry: true);
                        continue;
                    case "Scheduled":
                    case "Waiting":
                        if (r.ScheduledFor is not { } due || now < due) continue;
                        if (now - due > MissedAfter && !ApplyMissedPolicy(account, now))
                        {
                            changed = true;
                            continue;
                        }
                        await ExecuteAsync(account, now, isRetry: false);
                        continue;
                }
            }
            if (changed) _changed();
        }
        finally
        {
            _ticking = false;
        }
    }

    public void Arm(UsageProviderState account)
    {
        var r = account.Refresh;
        r.Enabled = true;
        r.Status = "Idle";
        r.Attempts = 0;
        r.NextRetryAt = null;
        r.LastError = "";
        Schedule(account, DateTimeOffset.Now);
        _changed();
    }

    public void Cancel(UsageProviderState account)
    {
        var r = account.Refresh;
        r.Enabled = false;
        r.Status = "Idle";
        r.ScheduledFor = null;
        r.NextRetryAt = null;
        Log(account, "Canceled", "");
        _changed();
    }

    // "Try again" after a failure or a missed time: plan from now, still honoring the window check.
    public void Retry(UsageProviderState account)
    {
        var r = account.Refresh;
        r.Enabled = true;
        r.Attempts = 0;
        r.Status = "Scheduled";
        r.ScheduledFor = TruncateToSecond(Max(DateTimeOffset.Now, account.SessionResetAt));
        r.PlannedForReset = account.SessionResetAt;
        _changed();
    }

    private bool Schedule(UsageProviderState account, DateTimeOffset now)
    {
        var r = account.Refresh;
        var planned = Plan(account, r, now);
        if (planned == null)
        {
            r.Status = "Blocked";
            return true;
        }
        r.ScheduledFor = planned;
        r.PlannedForReset = account.SessionResetAt;
        r.Status = "Scheduled";
        r.Attempts = 0;
        r.NextRetryAt = null;
        return true;
    }

    // Returns true when the run should go ahead now.
    private bool ApplyMissedPolicy(UsageProviderState account, DateTimeOffset now)
    {
        var r = account.Refresh;
        switch (_state.Settings.Refresh.MissedPolicy)
        {
            case "WaitNext":
                if (r.Mode == "AtTime" && r.ScheduledFor is { } due)
                {
                    r.ScheduledFor = due.AddDays(Math.Ceiling((now - due).TotalDays));
                    r.Status = "Scheduled";
                    Log(account, "Missed", Loc.T("rf.log.movedNext", Formatters.LocalTime(r.ScheduledFor.Value)));
                }
                else
                {
                    r.Status = "Missed";
                    Log(account, "Missed", "");
                }
                return false;
            case "Skip":
                r.Status = "Missed";
                if (r.Repeat == "Once") r.Enabled = false;
                Log(account, "Missed", "");
                return false;
            default:
                return true;
        }
    }

    private async Task ExecuteAsync(UsageProviderState account, DateTimeOffset now, bool isRetry)
    {
        var r = account.Refresh;
        var key = $"{account.AccountKey}|{r.ScheduledFor:yyyyMMddHHmm}";
        if (!isRetry && r.LastRunKey == key && r.Status is "Running" or "Success") return;
        _running.Add(account.AccountKey);
        try
        {
            // Confirm with fresh (token-free) usage data that a new window can really start now.
            await _refreshUsage(true);
            var reset = account.SessionResetAt;
            if (reset > DateTimeOffset.Now + ResetTolerance)
            {
                r.ScheduledFor = TruncateToSecond(reset);
                r.PlannedForReset = reset;
                r.Status = "Waiting";
                Log(account, "Postponed", Loc.T("rf.log.postponed", reset.ToLocalTime().ToString("HH:mm")));
                _changed();
                return;
            }

            r.Status = "Running";
            r.LastRunKey = key;
            r.LastRunAt = DateTimeOffset.Now;
            r.Attempts++;
            _changed();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await _runner.RunAsync(account, cts.Token);
            if (result.Success)
            {
                r.Status = "Success";
                r.LastSuccessAt = DateTimeOffset.Now;
                r.LastError = "";
                r.LastErrorKind = "";
                r.Attempts = 0;
                r.NextRetryAt = null;
                if (r.Repeat == "Once") r.Enabled = false;
                Log(account, "Success", result.CommandLine, result.DurationMs);
                _notify(Loc.T("rf.notify.successTitle", account.DisplayName), Loc.T("rf.notify.successBody", DateTimeOffset.Now.ToString("HH:mm")));
                // Pick up the new window's reset time.
                await _refreshUsage(true);
                r.PlannedForReset = r.Repeat == "Once" ? account.SessionResetAt : r.PlannedForReset;
            }
            else
            {
                r.LastError = result.Message;
                r.LastErrorKind = result.ErrorKind;
                var retryable = result.ErrorKind is not (RefreshError.NoCli or RefreshError.LoginExpired or RefreshError.Canceled);
                var settings = _state.Settings.Refresh;
                if (retryable && settings.RetryEnabled && r.Attempts - 1 < settings.RetryMax)
                {
                    r.Status = "RetryWaiting";
                    r.NextRetryAt = DateTimeOffset.Now.AddMinutes(Math.Max(1, settings.RetryIntervalMinutes));
                }
                else
                {
                    r.Status = "Failed";
                    _notify(Loc.T("rf.notify.failTitle", account.DisplayName), Loc.T("rf.err." + (string.IsNullOrEmpty(result.ErrorKind) ? RefreshError.Other : result.ErrorKind)));
                }
                Log(account, "Failed", $"{Loc.T("rf.err." + (string.IsNullOrEmpty(result.ErrorKind) ? RefreshError.Other : result.ErrorKind))} · {result.Message}", result.DurationMs);
            }
            _changed();
        }
        finally
        {
            _running.Remove(account.AccountKey);
        }
    }

    // "Renewable" heads-up and at-reset notifications (independent of scheduling).
    private bool HandleNotifications(UsageProviderState account, DateTimeOffset now)
    {
        var r = account.Refresh;
        if (!r.NotifyOnReset || account.Status == "NOT_SIGNED_IN") return false;
        var reset = account.SessionResetAt;
        var pre = _state.Settings.Refresh.PreNotifyMinutes;
        if (pre > 0 && reset > now && reset - now <= TimeSpan.FromMinutes(pre) && r.PreNotifiedResetFor != reset)
        {
            r.PreNotifiedResetFor = reset;
            _notify(Loc.T("rf.notify.preTitle", account.DisplayName, (int)Math.Ceiling((reset - now).TotalMinutes)), Loc.T("rf.notify.preBody", reset.ToLocalTime().ToString("HH:mm")));
            return true;
        }
        if (reset <= now && now - reset < TimeSpan.FromMinutes(10) && r.NotifiedResetFor != reset)
        {
            r.NotifiedResetFor = reset;
            var scheduled = r.Enabled && r.ScheduledFor is { } at ? Loc.T("rf.notify.readyScheduled", at.ToLocalTime().ToString("HH:mm")) : Loc.T("rf.notify.readyBody");
            _notify(Loc.T("rf.notify.readyTitle", account.DisplayName), scheduled);
            return true;
        }
        return false;
    }

    private void Log(UsageProviderState account, string outcome, string detail, int durationMs = 0)
    {
        _state.RefreshLog.Add(new RefreshLogEntry
        {
            At = DateTimeOffset.Now,
            AccountKey = account.AccountKey,
            Title = $"{account.DisplayName} · {account.AccountName}",
            Outcome = outcome,
            Detail = detail,
            DurationMs = durationMs
        });
        if (_state.RefreshLog.Count > 100) _state.RefreshLog.RemoveRange(0, _state.RefreshLog.Count - 100);
    }

    // ---- time helpers

    private static DateTimeOffset NextTimeOfDay(string hhmm, DateTimeOffset now)
    {
        var time = ParseTime(hhmm);
        var local = now.ToLocalTime();
        var candidate = new DateTimeOffset(local.Date + time, local.Offset);
        return candidate >= now ? candidate : candidate.AddDays(1);
    }

    // Moves a time into the allowed daily window [start, end) (the window may cross midnight).
    private static DateTimeOffset IntoWindow(DateTimeOffset at, string start, string end)
    {
        var s = ParseTime(start);
        var e = ParseTime(end);
        var local = at.ToLocalTime();
        var tod = local.TimeOfDay;
        var inside = s <= e ? tod >= s && tod < e : tod >= s || tod < e;
        if (inside) return at;
        var candidate = new DateTimeOffset(local.Date + s, local.Offset);
        return candidate > at ? candidate : candidate.AddDays(1);
    }

    public static TimeSpan ParseTime(string hhmm) =>
        TimeSpan.TryParse(hhmm, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : new TimeSpan(20, 0, 0);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset TruncateToSecond(DateTimeOffset t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);
}
