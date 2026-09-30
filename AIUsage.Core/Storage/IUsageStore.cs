namespace AIUsage.Core.Storage;

// What the feature needs from persistence. The standalone app backs it with state.json + history.jsonl;
// a Host widget keeps its feature state in the Host's state store and shares only the history file.
public interface IUsageStore
{
    string DataDirectory { get; }
    AppState LoadState();
    void SaveState(AppState state);
    IReadOnlyList<UsageSnapshot> LoadHistory();
    bool AppendHistory(AppState state, bool force = false);
}
