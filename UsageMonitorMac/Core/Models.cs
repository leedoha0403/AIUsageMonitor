namespace UsageMonitorMac.Core;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, UsageProviderState> Providers { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<RefreshLogEntry> RefreshLog { get; set; } = new();
}

public sealed class AppSettings
{
    public string Language { get; set; } = "Korean";
    public string CollectionLevel { get; set; } = "Standard";
    public int RefreshSeconds { get; set; } = 60;
    public string DisplayUsageAs { get; set; } = "Remaining";
    public string HistoryRange { get; set; } = "1D";
    public RefreshSettings Refresh { get; set; } = new();
}

public sealed class UsageProviderState
{
    public string AccountKey { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccountName { get; set; } = "Personal";
    public bool Enabled { get; set; } = true;
    public string ConfigDirectory { get; set; } = "";
    public string Plan { get; set; } = "Unknown";
    public string Status { get; set; } = "READY";
    public string Source { get; set; } = "Manual";
    public string Confidence { get; set; } = "Low";
    public int SessionUsagePercent { get; set; }
    public DateTimeOffset SessionResetAt { get; set; } = DateTimeOffset.Now.AddHours(5);
    public int WeeklyUsagePercent { get; set; }
    public DateTimeOffset WeeklyResetAt { get; set; } = DateTimeOffset.Now.AddDays(7);
    public ExtraUsageInfo? ExtraUsage { get; set; }
    public string? CreditsBalance { get; set; }
    public List<ModelUsage> ModelBreakdown { get; set; } = new();
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string Message { get; set; } = "Waiting for collection";
    public ProviderCapabilities Capabilities { get; set; } = new();
    public Dictionary<string, FieldSource> FieldSources { get; set; } = new();
    public List<CollectorInfo> Collectors { get; set; } = new();
    public AccountRefresh Refresh { get; set; } = new();
}

public sealed class ProviderCapabilities
{
    public bool SessionUsage { get; set; } = true;
    public bool WeeklyUsage { get; set; } = true;
    public bool Credits { get; set; }
    public bool MultiAccount { get; set; }
    public bool ModelBreakdown { get; set; }
    public bool ExtraUsage { get; set; }
}

public sealed class ModelUsage
{
    public string Name { get; set; } = "";
    public int Percent { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
}

public sealed class ExtraUsageInfo
{
    public bool IsEnabled { get; set; }
    public decimal? UsedDollars { get; set; }
    public decimal? MonthlyLimitDollars { get; set; }
    public int? UtilizationPercent { get; set; }
}

public sealed class FieldSource
{
    public string Source { get; set; } = "Manual";
    public string Confidence { get; set; } = "Low";
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class CollectorInfo
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Level { get; set; } = "Safe";
    public int MinIntervalSeconds { get; set; }
    public string Status { get; set; } = "DISABLED";
    public bool TokenFreeVerified { get; set; }
    public string Verification { get; set; } = "";
    public int? LatencyMs { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class UsageSnapshot
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Provider { get; set; } = "";
    public string AccountKey { get; set; } = "";
    public string Account { get; set; } = "Personal";
    public int SessionUsagePercent { get; set; }
    public int WeeklyUsagePercent { get; set; }
    public DateTimeOffset SessionResetAt { get; set; }
    public DateTimeOffset WeeklyResetAt { get; set; }
    public string Source { get; set; } = "Manual";
    public string Confidence { get; set; } = "Low";
    public string EffectiveKey => string.IsNullOrEmpty(AccountKey) ? Provider : AccountKey;
}

public sealed class RefreshSettings
{
    public bool RetryEnabled { get; set; } = true;
    public int RetryIntervalMinutes { get; set; } = 1;
    public int RetryMax { get; set; } = 3;
    public string MissedPolicy { get; set; } = "RunOnWake";
}

public sealed class AccountRefresh
{
    public bool Enabled { get; set; }
    public string CostMode { get; set; } = "Minimal";
    public string Model { get; set; } = "";
    public string PromptMode { get; set; } = "Default";
    public string Prompt { get; set; } = "hi";
    public string Status { get; set; } = "Idle";
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string LastError { get; set; } = "";
    public string LastErrorKind { get; set; } = "";
}

public sealed class RefreshLogEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string AccountKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Detail { get; set; } = "";
    public int DurationMs { get; set; }
}
