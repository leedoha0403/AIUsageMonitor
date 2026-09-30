namespace AIUsage.Core.Storage;

// Fills blanks left by older or partial state and re-syncs provider definitions, so any source of state
// (state.json, a Host snapshot) ends up with a complete AppState.
public static class StateNormalizer
{
    public static AppState Normalize(AppState state)
    {
        var defaults = new AppSettings();
        state.Settings ??= new AppSettings();
        state.Providers ??= new Dictionary<string, UsageProviderState>();
        state.RefreshLog ??= new List<RefreshLogEntry>();
        state.Settings.Language = string.IsNullOrWhiteSpace(state.Settings.Language) ? defaults.Language : state.Settings.Language;
        state.Settings.WidgetMode = string.IsNullOrWhiteSpace(state.Settings.WidgetMode) ? defaults.WidgetMode : state.Settings.WidgetMode;
        state.Settings.WindowVersion = string.IsNullOrWhiteSpace(state.Settings.WindowVersion) ? defaults.WindowVersion : state.Settings.WindowVersion;
        state.Settings.Theme = string.IsNullOrWhiteSpace(state.Settings.Theme) ? defaults.Theme : state.Settings.Theme;
        state.Settings.DisplayUsageAs = string.IsNullOrWhiteSpace(state.Settings.DisplayUsageAs) ? defaults.DisplayUsageAs : state.Settings.DisplayUsageAs;
        state.Settings.CollectionLevel = string.IsNullOrWhiteSpace(state.Settings.CollectionLevel) ? defaults.CollectionLevel : state.Settings.CollectionLevel;
        state.Settings.HistoryRange = string.IsNullOrWhiteSpace(state.Settings.HistoryRange) ? defaults.HistoryRange : state.Settings.HistoryRange;
        state.Settings.HistoryChartMode = string.IsNullOrWhiteSpace(state.Settings.HistoryChartMode) ? defaults.HistoryChartMode : state.Settings.HistoryChartMode;
        if (state.Settings.NotificationThresholds is not { Count: > 0 }) state.Settings.NotificationThresholds = defaults.NotificationThresholds;
        state.Settings.CustomTheme ??= defaults.CustomTheme;
        state.Settings.CustomThemePresets ??= new();
        state.Settings.Refresh ??= new RefreshSettings();

        foreach (var (key, account) in state.Providers.ToList())
        {
            if (string.IsNullOrWhiteSpace(account.AccountKey)) account.AccountKey = key;
            if (string.IsNullOrWhiteSpace(account.ProviderId)) account.ProviderId = key;
            try
            {
                Defaults.SyncDefinition(account);
            }
            catch (ArgumentException)
            {
                state.Providers.Remove(key);
            }
        }
        foreach (var id in Defaults.ProviderOrder)
        {
            if (!state.Providers.ContainsKey(id)) state.Providers[id] = Defaults.CreateAccount(id, id, "Personal");
        }
        state.SchemaVersion = new AppState().SchemaVersion;
        return state;
    }
}
