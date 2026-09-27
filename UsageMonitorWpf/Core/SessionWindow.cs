namespace UsageMonitorWpf.Core;

// Whether a 5H usage window is actually running.
// Once a window ends and nothing has been used yet, Codex reports "0% used, resets in 5 hours" and that
// reset time keeps moving with the clock. That is a window that has not started, not an active one.
public static class SessionWindow
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(5);
    private static readonly TimeSpan IdleTolerance = TimeSpan.FromMinutes(3);

    public static bool IsActive(UsageProviderState account, DateTimeOffset now)
    {
        var reset = account.SessionResetAt;
        if (reset <= now) return false;
        if (account.SessionUsagePercent == 0 && reset - now >= Length - IdleTolerance) return false;
        return true;
    }

    // Same as IsActive, but also counts a window we just started ourselves via scheduled refresh: its
    // first poll looks identical to the "0% used, resets in 5h" idle placeholder IsActive filters out,
    // so without this a just-triggered refresh would immediately be reported as "renewable now" again.
    public static bool IsActive(UsageProviderState account, AccountRefresh refresh, DateTimeOffset now) =>
        IsActive(account, now) || (refresh.LastSuccessAt is { } ok && now < ok + Length);

    // When a new window can be started: now if none is running, otherwise the running window's reset.
    public static DateTimeOffset RenewableAt(UsageProviderState account, DateTimeOffset now) =>
        IsActive(account, now) ? account.SessionResetAt : now;
}
