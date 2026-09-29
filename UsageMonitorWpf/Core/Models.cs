namespace UsageMonitorWpf.Core;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 3;
    public AppSettings Settings { get; set; } = new();
    // Keyed by account key ("claude", "codex", "copilot", or "<provider>-<id>" for extra accounts).
    public Dictionary<string, UsageProviderState> Providers { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public string WidgetDockEdge { get; set; } = "None";
    public bool WidgetFolded { get; set; }
    public double? ChipsLeft { get; set; }
    public double? ChipsTop { get; set; }
    public List<RefreshLogEntry> RefreshLog { get; set; } = new();
}

public sealed class AppSettings
{
    public string Language { get; set; } = "Korean";
    public string WidgetMode { get; set; } = "Normal";
    public string WindowVersion { get; set; } = "Expanded";
    public string Theme { get; set; } = "System";
    public string DisplayUsageAs { get; set; } = "Remaining";
    public int RefreshSeconds { get; set; } = 60;
    public bool AlwaysOnTop { get; set; } = true;
    public string CollectionLevel { get; set; } = "Standard";
    public bool AllowUnverifiedCollectors { get; set; }
    public string FavoriteProvider { get; set; } = "claude";
    public string FavoriteAccount { get; set; } = "";
    public int WarningThreshold { get; set; } = 80;
    public List<int> NotificationThresholds { get; set; } = [70, 80, 90, 95, 100];
    public bool NotificationsEnabled { get; set; } = true;
    public bool ShowTaskbarChips { get; set; } = true;
    public string HistoryRange { get; set; } = "1D";
    public double WidgetOpacity { get; set; } = 1.0;
    public double ChipsOpacity { get; set; } = 1.0;
    public bool HoverOpaque { get; set; } = true;
    public CustomThemeSettings CustomTheme { get; set; } = new();
    public List<CustomThemePreset> CustomThemePresets { get; set; } = new();
    public RefreshSettings Refresh { get; set; } = new();
}

public sealed class CustomThemeSettings
{
    public string Ink { get; set; } = "#3B2630";
    public string Muted { get; set; } = "#8E6576";
    public string Accent { get; set; } = "#FF75A6";
    public string Panel { get; set; } = "#FFF9FC";
    public string Canvas { get; set; } = "#FFF1F7";
    public string Line { get; set; } = "#2EFFB8D2";
    public string Header { get; set; } = "#8B2C55";
    public string HeaderStart { get; set; } = "#FFE7F1";
    public string HeaderMiddle { get; set; } = "#FFE0A8";
    public string HeaderEnd { get; set; } = "#B9F6E4";

    public CustomThemeSettings Clone() => new()
    {
        Ink = Ink,
        Muted = Muted,
        Accent = Accent,
        Panel = Panel,
        Canvas = Canvas,
        Line = Line,
        Header = Header,
        HeaderStart = HeaderStart,
        HeaderMiddle = HeaderMiddle,
        HeaderEnd = HeaderEnd
    };
}

public sealed class CustomThemePreset
{
    public string Name { get; set; } = "";
    public CustomThemeSettings Theme { get; set; } = new();
}

public sealed class UsageProviderState
{
    public string AccountKey { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccountName { get; set; } = "Personal";
    public bool Enabled { get; set; } = true;
    // Display only: a hidden account keeps its login and is still collected.
    public bool ShowInMini { get; set; } = true;
    public bool ShowInDashboard { get; set; } = true;
    // Optional CLAUDE_CONFIG_DIR / CODEX_HOME style directory for this account.
    public string ConfigDirectory { get; set; } = "";
    // Copilot only: the CLI keeps several GitHub logins in one folder. Empty = the CLI's current login.
    public string Login { get; set; } = "";
    public string Plan { get; set; } = "Manual";
    public string Status { get; set; } = "READY";
    public string Source { get; set; } = "Manual";
    public string Confidence { get; set; } = "Low";
    public int SessionUsagePercent { get; set; }
    public DateTimeOffset SessionResetAt { get; set; } = DateTimeOffset.Now.AddHours(5);
    public int WeeklyUsagePercent { get; set; }
    public DateTimeOffset WeeklyResetAt { get; set; } = DateTimeOffset.Now.AddDays(7);
    public decimal? ExtraUsageCost { get; set; }
    public ExtraUsageInfo? ExtraUsage { get; set; }
    public string? CreditsBalance { get; set; }
    public List<ModelUsage> ModelBreakdown { get; set; } = new();
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastSuccessAt { get; set; }
    // Set when a poll observes SessionUsagePercent drop to 0 from a nonzero value: a directly witnessed
    // reset (manual or automatic), as opposed to Codex's "0% used, resets in 5h" idle placeholder that
    // never actually had any usage. See SessionWindow.IsActive.
    public DateTimeOffset? SessionResetObservedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string Message { get; set; } = Loc.Msg("msg.waiting");
    public int LastNotifiedThreshold { get; set; }
    public DateTimeOffset? NotifiedWindowResetAt { get; set; }
    public ProviderCapabilities Capabilities { get; set; } = new();
    public Dictionary<string, FieldSource> FieldSources { get; set; } = new();
    public List<CollectorInfo> Collectors { get; set; } = new();
    public AccountRefresh Refresh { get; set; } = new();
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

public sealed class ProviderCapabilities
{
    // False for providers without a 5H window (Copilot): the long window becomes the primary one.
    public bool SessionUsage { get; set; } = true;
    public bool WeeklyUsage { get; set; } = true;
    // Length of the window kept in WeeklyUsagePercent/WeeklyResetAt: "Weekly" or "Monthly".
    public string LongWindow { get; set; } = "Weekly";
    public bool Credits { get; set; }
    public bool MultiAccount { get; set; }
    public bool WslCredentialDetection { get; set; }
    public bool ModelBreakdown { get; set; }
    public bool ExtraUsage { get; set; }
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
    // Minimum collection level that runs this collector: Safe, Standard or Deep.
    public string Level { get; set; } = "Safe";
    public int MinIntervalSeconds { get; set; }
    public string Status { get; set; } = "DISABLED";
    public bool TokenFreeVerified { get; set; }
    public string Verification { get; set; } = "Not verified as token-free";
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

    // History written before multi-account support has no AccountKey; the provider id was the key.
    public string EffectiveKey => string.IsNullOrEmpty(AccountKey) ? Provider : AccountKey;
}

// Global options for scheduled refresh (starting a new usage window with a minimal CLI request).
public sealed class RefreshSettings
{
    public bool RetryEnabled { get; set; } = true;
    public int RetryIntervalMinutes { get; set; } = 1;
    public int RetryMax { get; set; } = 3;
    // RunOnWake | WaitNext | Skip: what to do when the PC slept through the scheduled time.
    public string MissedPolicy { get; set; } = "RunOnWake";
    // Minutes before the window becomes renewable to send a heads-up (0 = off).
    public int PreNotifyMinutes { get; set; }
}

public sealed class AccountRefresh
{
    // Scheduling
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "AtReset";          // AtReset | AtTime | AfterReset
    public string Time { get; set; } = "20:00";            // AtTime
    public int DelayMinutes { get; set; } = 60;            // AfterReset
    public string Repeat { get; set; } = "Once";           // Once | Every | Window
    public string WindowStart { get; set; } = "09:00";     // Repeat = Window
    public string WindowEnd { get; set; } = "02:00";
    // Request
    public string CostMode { get; set; } = "Minimal";      // Minimal | Custom
    public string Model { get; set; } = "";                // CostMode = Custom
    public string PromptMode { get; set; } = "Default";    // Default | Custom
    public string Prompt { get; set; } = "hi";
    // Notifications
    public bool NotifyOnReset { get; set; }
    public DateTimeOffset? NotifiedResetFor { get; set; }
    public DateTimeOffset? PreNotifiedResetFor { get; set; }
    // Runtime state
    public string Status { get; set; } = "Idle";           // Idle | Scheduled | Waiting | Running | RetryWaiting | Success | Failed | Missed | Blocked
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? PlannedForReset { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string LastRunKey { get; set; } = "";
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
