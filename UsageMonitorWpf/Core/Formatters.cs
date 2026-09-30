namespace UsageMonitorWpf.Core;

public static class Formatters
{
    public static string Countdown(DateTimeOffset target)
    {
        var span = target.ToLocalTime() - DateTimeOffset.Now;
        if (span.TotalSeconds <= 0) return Loc.T("fmt.resetPending");
        if (span.TotalDays >= 1) return Loc.T("fmt.dayHour", Math.Floor(span.TotalDays), span.Hours);
        return $"{Math.Floor(span.TotalHours):00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    // Dock form: "3d 03:35" once a day or more is left (3 days, 3 hours, 35 minutes), otherwise like Countdown.
    public static string DockCountdown(DateTimeOffset target)
    {
        var span = target.ToLocalTime() - DateTimeOffset.Now;
        if (span.TotalDays >= 1) return $"{Math.Floor(span.TotalDays):0}d {span.Hours:00}:{span.Minutes:00}";
        return Countdown(target);
    }

    // Compact form for chips: "4:15" or "3d 03:35".
    public static string ShortCountdown(DateTimeOffset target)
    {
        var span = target.ToLocalTime() - DateTimeOffset.Now;
        if (span.TotalSeconds <= 0) return Loc.T("fmt.resetPending");
        if (span.TotalDays >= 1) return $"{Math.Floor(span.TotalDays):0}d {span.Hours:00}:{span.Minutes:00}";
        return $"{Math.Floor(span.TotalHours):0}:{span.Minutes:00}";
    }

    public static string ResetState(DateTimeOffset target)
    {
        var span = target.ToLocalTime() - DateTimeOffset.Now;
        if (span.TotalSeconds <= 0) return Loc.T("fmt.resetPending");
        if (span.TotalMinutes < 15) return Loc.T("fmt.resetSoon");
        if (span.TotalHours < 1) return Loc.T("fmt.attention");
        return Loc.T("fmt.normal");
    }

    public static string LocalTime(DateTimeOffset target) => target.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static string Ago(DateTimeOffset? at)
    {
        if (!at.HasValue) return Loc.T("fmt.never");
        var span = DateTimeOffset.Now - at.Value;
        if (span.TotalSeconds < 60) return Loc.T("fmt.secAgo", Math.Max(0, (int)span.TotalSeconds));
        if (span.TotalMinutes < 60) return Loc.T("fmt.minAgo", (int)span.TotalMinutes);
        if (span.TotalHours < 24) return Loc.T("fmt.hourAgo", (int)span.TotalHours, span.Minutes);
        return Loc.T("fmt.dayAgo", (int)span.TotalDays);
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return Loc.T("fmt.lt1m");
        if (span.TotalHours < 1) return Loc.T("fmt.min", (int)span.TotalMinutes);
        if (span.TotalDays < 1) return Loc.T("fmt.hourMin", (int)span.TotalHours, span.Minutes);
        return Loc.T("fmt.dayHour", (int)span.TotalDays, span.Hours);
    }

    public static string UsageState(int used)
    {
        return used switch
        {
            >= 95 => "Critical",
            >= 85 => "High",
            >= 70 => "Notice",
            _ => "Normal"
        };
    }
}
