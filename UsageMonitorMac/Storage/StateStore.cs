using System.Text.Json;
using UsageMonitorMac.Core;

namespace UsageMonitorMac.Storage;

public sealed class StateStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly JsonSerializerOptions _line = new();
    private List<UsageSnapshot>? _history;

    public StateStore(string appName)
    {
        DataDirectory = ResolveDataDirectory(appName);
        StatePath = Path.Combine(DataDirectory, "state.json");
        HistoryPath = Path.Combine(DataDirectory, "history.jsonl");
    }

    public string DataDirectory { get; }
    public string StatePath { get; }
    public string HistoryPath { get; }

    public AppState LoadState()
    {
        if (!File.Exists(StatePath)) return Defaults.CreateState();
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), _json) ?? Defaults.CreateState();
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
        File.Move(temp, StatePath, overwrite: true);
    }

    public IReadOnlyList<UsageSnapshot> LoadHistory()
    {
        if (_history != null) return _history;
        var list = new List<UsageSnapshot>();
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
        RewriteHistory();
        return _history;
    }

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
        File.Move(temp, HistoryPath, overwrite: true);
    }

    private static string ResolveDataDirectory(string appName)
    {
        var overrideDir = Environment.GetEnvironmentVariable("USAGE_MONITOR_DATA_DIR");
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(overrideDir) ? null : overrideDir,
            MacApplicationSupport(appName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName),
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

    private static string? MacApplicationSupport(string appName)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", appName);
    }
}
