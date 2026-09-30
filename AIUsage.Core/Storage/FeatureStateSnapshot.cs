using System.Text.Json;

namespace AIUsage.Core.Storage;

// The part of AppState that belongs to the usage feature itself (including the taskbar chips). Window/dock placement, theme, tray and
// other shell options are owned by whoever hosts the feature and are deliberately not part of it.
public sealed class FeatureStateSnapshot
{
    // Bump when this shape changes and add a step to Upgrade().
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public Dictionary<string, UsageProviderState> Providers { get; set; } = new();
    public List<RefreshLogEntry> RefreshLog { get; set; } = new();
    public FeatureSettings Settings { get; set; } = new();

    public static FeatureStateSnapshot Capture(AppState state) => new()
    {
        Providers = state.Providers,
        RefreshLog = state.RefreshLog,
        Settings = new FeatureSettings
        {
            Language = state.Settings.Language,
            DisplayUsageAs = state.Settings.DisplayUsageAs,
            WidgetMode = state.Settings.WidgetMode,
            ShowTaskbarChips = state.Settings.ShowTaskbarChips,
            ChipsOpacity = state.Settings.ChipsOpacity,
            ChipsLeft = state.ChipsLeft,
            ChipsTop = state.ChipsTop,
            RefreshSeconds = state.Settings.RefreshSeconds,
            CollectionLevel = state.Settings.CollectionLevel,
            AllowUnverifiedCollectors = state.Settings.AllowUnverifiedCollectors,
            FavoriteProvider = state.Settings.FavoriteProvider,
            FavoriteAccount = state.Settings.FavoriteAccount,
            WarningThreshold = state.Settings.WarningThreshold,
            NotificationThresholds = state.Settings.NotificationThresholds,
            NotificationsEnabled = state.Settings.NotificationsEnabled,
            HistoryRange = state.Settings.HistoryRange,
            HistoryChartMode = state.Settings.HistoryChartMode,
            Refresh = state.Settings.Refresh
        }
    };

    // Builds a complete AppState: feature values from this snapshot, everything else at its default.
    public AppState ToAppState()
    {
        var state = new AppState
        {
            Providers = Providers,
            RefreshLog = RefreshLog
        };
        state.Settings.Language = Settings.Language;
        state.Settings.DisplayUsageAs = Settings.DisplayUsageAs;
        state.Settings.WidgetMode = Settings.WidgetMode;
        state.Settings.ShowTaskbarChips = Settings.ShowTaskbarChips;
        state.Settings.ChipsOpacity = Settings.ChipsOpacity;
        state.ChipsLeft = Settings.ChipsLeft;
        state.ChipsTop = Settings.ChipsTop;
        state.Settings.RefreshSeconds = Settings.RefreshSeconds;
        state.Settings.CollectionLevel = Settings.CollectionLevel;
        state.Settings.AllowUnverifiedCollectors = Settings.AllowUnverifiedCollectors;
        state.Settings.FavoriteProvider = Settings.FavoriteProvider;
        state.Settings.FavoriteAccount = Settings.FavoriteAccount;
        state.Settings.WarningThreshold = Settings.WarningThreshold;
        state.Settings.NotificationThresholds = Settings.NotificationThresholds;
        state.Settings.NotificationsEnabled = Settings.NotificationsEnabled;
        state.Settings.HistoryRange = Settings.HistoryRange;
        state.Settings.HistoryChartMode = Settings.HistoryChartMode;
        state.Settings.Refresh = Settings.Refresh;
        return StateNormalizer.Normalize(state);
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    // Returns null when the payload is unusable (corrupt, or written by a newer version); callers start fresh.
    public static FeatureStateSnapshot? Deserialize(int version, string json)
    {
        if (version > CurrentVersion || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return Upgrade(version, JsonSerializer.Deserialize<FeatureStateSnapshot>(json, Json));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FeatureStateSnapshot? Upgrade(int fromVersion, FeatureStateSnapshot? snapshot)
    {
        // No older shapes yet.
        return snapshot;
    }
}

public sealed class FeatureSettings
{
    public string Language { get; set; } = "Korean";
    public string DisplayUsageAs { get; set; } = "Remaining";
    // Mini summary density (Compact / Normal / Detailed); part of the feature, not the Host.
    public string WidgetMode { get; set; } = "Normal";
    // The taskbar chips belong to the feature: whoever owns the widget shows them, from the same setting and spot.
    public bool ShowTaskbarChips { get; set; } = true;
    public double ChipsOpacity { get; set; } = 1.0;
    public double? ChipsLeft { get; set; }
    public double? ChipsTop { get; set; }
    public int RefreshSeconds { get; set; } = 60;
    public string CollectionLevel { get; set; } = "Standard";
    public bool AllowUnverifiedCollectors { get; set; }
    public string FavoriteProvider { get; set; } = "claude";
    public string FavoriteAccount { get; set; } = "";
    public int WarningThreshold { get; set; } = 80;
    public List<int> NotificationThresholds { get; set; } = [70, 80, 90, 95, 100];
    public bool NotificationsEnabled { get; set; } = true;
    public string HistoryRange { get; set; } = "1D";
    public string HistoryChartMode { get; set; } = "Session";
    public RefreshSettings Refresh { get; set; } = new();
}
