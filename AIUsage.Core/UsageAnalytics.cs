namespace AIUsage.Core;

public sealed record VelocityInfo(double PerHour, int Last30Minutes, TimeSpan Span);

public sealed record ResetWindowSummary(DateTimeOffset ResetAt, int Peak, bool LimitReached);

// Derived, unofficial numbers computed from local history. Everything here is labelled "Estimated" in the UI.
public static class UsageAnalytics
{
    private static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    public static List<UsageSnapshot> CurrentWindow(IEnumerable<UsageSnapshot> history, UsageProviderState account)
    {
        var start = account.SessionResetAt - SessionWindow;
        return history
            .Where(x => x.EffectiveKey == account.AccountKey && x.Timestamp >= start && SameWindow(x.SessionResetAt, account.SessionResetAt))
            .OrderBy(x => x.Timestamp)
            .ToList();
    }

    public static VelocityInfo? Velocity(IReadOnlyList<UsageSnapshot> window, UsageProviderState account)
    {
        var now = DateTimeOffset.Now;
        var recent = window.Where(x => x.Timestamp >= now.AddMinutes(-60)).ToList();
        if (recent.Count < 2) return null;
        var first = recent[0];
        var span = now - first.Timestamp;
        if (span < TimeSpan.FromMinutes(5)) return null;
        var perHour = (account.SessionUsagePercent - first.SessionUsagePercent) / span.TotalHours;
        var anchor30 = window.LastOrDefault(x => x.Timestamp <= now.AddMinutes(-30)) ?? first;
        return new VelocityInfo(Math.Max(0, perHour), account.SessionUsagePercent - anchor30.SessionUsagePercent, span);
    }

    public static string Forecast(VelocityInfo? velocity, UsageProviderState account)
    {
        var now = DateTimeOffset.Now;
        if (account.SessionUsagePercent >= 100)
        {
            return account.SessionResetAt > now ? Loc.T("an.limitReached", Formatters.Duration(account.SessionResetAt - now)) : Loc.T("an.limitReachedPending");
        }
        if (velocity == null) return Loc.T("pv.noHistory");
        if (velocity.PerHour < 0.5) return Loc.T("an.flat");
        var eta = now.AddHours((100 - account.SessionUsagePercent) / velocity.PerHour);
        return eta < account.SessionResetAt
            ? Loc.T("an.limitIn", Formatters.Duration(eta - now), eta.ToLocalTime().ToString("HH:mm"))
            : Loc.T("an.resetsFirst", Math.Min(100, account.SessionUsagePercent + velocity.PerHour * (account.SessionResetAt - now).TotalHours).ToString("0"));
    }

    public static List<string> Timeline(IReadOnlyList<UsageSnapshot> window, UsageProviderState account)
    {
        var lines = new List<string>();
        var start = account.SessionResetAt - SessionWindow;
        lines.Add($"{start.ToLocalTime():HH:mm}  {Loc.T("an.windowStarted")}");
        var lastShown = -100;
        var points = new List<UsageSnapshot>();
        foreach (var item in window)
        {
            if (Math.Abs(item.SessionUsagePercent - lastShown) < 5) continue;
            points.Add(item);
            lastShown = item.SessionUsagePercent;
        }
        // Keep the timeline short: first and the most recent milestones.
        if (points.Count > 7) points = points.Take(1).Concat(points.TakeLast(6)).ToList();
        lines.AddRange(points.Select(p => $"{p.Timestamp.ToLocalTime():HH:mm}  {p.SessionUsagePercent}%{(p.SessionUsagePercent >= 100 ? "  " + Loc.T("an.limit") : "")}"));
        var now = DateTimeOffset.Now;
        lines.Add(account.SessionResetAt > now
            ? $"{account.SessionResetAt.ToLocalTime():HH:mm}  {Loc.T("an.resetIn", Formatters.Duration(account.SessionResetAt - now))}"
            : $"{account.SessionResetAt.ToLocalTime():HH:mm}  {Loc.T("an.reset")}");
        return lines;
    }

    public static List<ResetWindowSummary> ResetHistory(IEnumerable<UsageSnapshot> history, string accountKey, TimeSpan range)
    {
        var now = DateTimeOffset.Now;
        return history
            .Where(x => x.EffectiveKey == accountKey && x.Timestamp >= now - range && x.SessionResetAt <= now && x.SessionResetAt > DateTimeOffset.MinValue.AddYears(1))
            .GroupBy(x => new DateTimeOffset(x.SessionResetAt.UtcDateTime.Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero))
            .Select(g => new ResetWindowSummary(g.Key, g.Max(x => x.SessionUsagePercent), g.Any(x => x.SessionUsagePercent >= 100)))
            .OrderByDescending(x => x.ResetAt)
            .ToList();
    }

    // Reset timestamps from different collectors can differ by seconds for the same window.
    private static bool SameWindow(DateTimeOffset a, DateTimeOffset b) => (a - b).Duration() < TimeSpan.FromMinutes(10);
}
