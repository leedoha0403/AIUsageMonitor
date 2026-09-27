namespace UsageMonitorMac.Core;

public static class Formatters
{
    public static string ShortCountdown(DateTimeOffset target)
    {
        var span = target.ToLocalTime() - DateTimeOffset.Now;
        if (span.TotalSeconds <= 0) return "reset pending";
        if (span.TotalDays >= 1) return $"{Math.Floor(span.TotalDays):0}d {span.Hours:0}h";
        return $"{Math.Floor(span.TotalHours):0}:{span.Minutes:00}";
    }

    public static string LocalTime(DateTimeOffset target) => target.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static string UsageState(int used) => used switch
    {
        >= 95 => "Critical",
        >= 85 => "High",
        >= 70 => "Notice",
        _ => "Normal"
    };
}
