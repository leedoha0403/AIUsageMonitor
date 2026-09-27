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

    // When a new window can be started: now if none is running, otherwise the running window's reset.
    public static DateTimeOffset RenewableAt(UsageProviderState account, DateTimeOffset now) =>
        IsActive(account, now) ? account.SessionResetAt : now;
}
