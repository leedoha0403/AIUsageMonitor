using System.IO;
using System.Text.Json;
using UsageMonitorWpf.Core;

namespace UsageMonitorWpf.Storage;

public sealed class StateStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly JsonSerializerOptions _line = new();
    private List<UsageSnapshot>? _history;

    public StateStore()
    {
        DataDirectory = ResolveDataDirectory();
        StatePath = Path.Combine(DataDirectory, "state.json");
        HistoryPath = Path.Combine(DataDirectory, "history.jsonl");
        LegacyHistoryPath = Path.Combine(DataDirectory, "history.json");
    }

    public string DataDirectory { get; }
    public string StatePath { get; }
    public string HistoryPath { get; }
    private string LegacyHistoryPath { get; }

    public AppState LoadState()
    {
        if (!File.Exists(StatePath)) return Defaults.CreateState();
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), _json) ?? Defaults.CreateState();
            var defaults = new AppSettings();
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
        catch
        {
            return Defaults.CreateState();
        }
    }

    public void SaveState(AppState state)
    {
        state.UpdatedAt = DateTimeOffset.Now;
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, _json));
        ReplaceFile(temp, StatePath);
    }

    public IReadOnlyList<UsageSnapshot> LoadHistory()
    {
        if (_history != null) return _history;
        var list = new List<UsageSnapshot>();
        if (File.Exists(LegacyHistoryPath))
        {
            try
            {
                list.AddRange(JsonSerializer.Deserialize<List<UsageSnapshot>>(File.ReadAllText(LegacyHistoryPath), _json) ?? []);
            }
            catch
            {
            }
        }
        if (File.Exists(HistoryPath))
        {
            foreach (var line in File.ReadLines(HistoryPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<UsageSnapshot>(line, _line) is { } item) list.Add(item);
                }
                catch
                {
                }
            }
        }

        _history = list.Where(x => x.Timestamp >= DateTimeOffset.Now - Retention).OrderBy(x => x.Timestamp).ToList();
        // Compact once per load: drops expired rows and migrates the old history.json.
        RewriteHistory();
        if (File.Exists(LegacyHistoryPath)) File.Move(LegacyHistoryPath, LegacyHistoryPath + ".migrated", overwrite: true);
        return _history;
    }

    // Append-only; a row is written when a value changed or at least 5 minutes passed for that account,
    // which keeps 30 days of multi-account history small.
    public bool AppendHistory(AppState state, bool force = false)
    {
        var history = (List<UsageSnapshot>)LoadHistory();
        var now = DateTimeOffset.Now;
        var added = new List<UsageSnapshot>();
        foreach (var p in state.Providers.Values.Where(a => a.Enabled))
        {
            var last = history.LastOrDefault(x => x.EffectiveKey == p.AccountKey);
            var changed = last == null || last.SessionUsagePercent != p.SessionUsagePercent || last.WeeklyUsagePercent != p.WeeklyUsagePercent ||
                          last.SessionResetAt != p.SessionResetAt || last.Source != p.Source;
            if (!force && !changed && now - last!.Timestamp < TimeSpan.FromMinutes(5)) continue;
            added.Add(new UsageSnapshot
            {
                Timestamp = now,
                Provider = p.ProviderId,
                AccountKey = p.AccountKey,
                Account = p.AccountName,
                SessionUsagePercent = p.SessionUsagePercent,
                WeeklyUsagePercent = p.WeeklyUsagePercent,
                SessionResetAt = p.SessionResetAt,
                WeeklyResetAt = p.WeeklyResetAt,
                Source = p.Source,
                Confidence = p.Confidence
            });
        }
        if (added.Count == 0) return false;

        history.AddRange(added);
        File.AppendAllLines(HistoryPath, added.Select(x => JsonSerializer.Serialize(x, _line)));
        if (history.Count > 0 && history[0].Timestamp < now - Retention - TimeSpan.FromDays(1))
        {
            history.RemoveAll(x => x.Timestamp < now - Retention);
            RewriteHistory();
        }
        return true;
    }

    private void RewriteHistory()
    {
        if (_history == null) return;
        var temp = HistoryPath + ".tmp";
        File.WriteAllLines(temp, _history.Select(x => JsonSerializer.Serialize(x, _line)));
        ReplaceFile(temp, HistoryPath);
    }

    // Antivirus/indexer briefly open a file that was just written, so replacing it right after the
    // previous save can fail with a sharing violation. Retry briefly; if it still fails, keep the old
    // file (the next save catches up) instead of letting the exception take the whole app down.
    private static void ReplaceFile(string temp, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 5)
                {
                    AppLog.Write($"could not replace {Path.GetFileName(target)}: {ex.Message}");
                    return;
                }
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static string ResolveDataDirectory()
    {
        // USAGE_MONITOR_DATA_DIR lets a second copy (e.g. a test build) run without touching the real data.
        var overrideDir = Environment.GetEnvironmentVariable("USAGE_MONITOR_DATA_DIR");
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(overrideDir) ? null : overrideDir,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UsageMonitorWpf"),
            Path.Combine(AppContext.BaseDirectory, ".usage-monitor")
        };

        foreach (var candidate in candidates.OfType<string>())
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
            }
        }
        throw new InvalidOperationException("No writable data directory is available.");
    }
}
