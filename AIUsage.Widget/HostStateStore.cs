using AIUsage.Core;
using AIUsage.Core.Storage;

namespace AIUsage.Widget;

// Feature state lives in the Host's widget state (via SaveStateAsync), not in the standalone app's state.json.
// Only usage history is a shared data file: it is append-only and written by whichever instance owns collection.
public sealed class HostStateStore : IUsageStore
{
    private readonly FeatureStateSnapshot? _restored;
    private readonly IUsageStore _files;
    private FeatureStateSnapshot? _latest;

    public HostStateStore(FeatureStateSnapshot? restored, IUsageStore files)
    {
        _restored = restored;
        _files = files;
        _latest = restored;
    }

    public string DataDirectory => _files.DataDirectory;

    // First run (nothing saved by the Host yet): adopt the standalone app's feature settings and accounts if any.
    public AppState LoadState() => _restored is null ? _files.LoadState() : _restored.ToAppState();

    public void SaveState(AppState state) => _latest = FeatureStateSnapshot.Capture(state);

    public IReadOnlyList<UsageSnapshot> LoadHistory() => _files.LoadHistory();

    public bool AppendHistory(AppState state, bool force = false) => _files.AppendHistory(state, force);

    // What the Host should persist for this instance, or null when there is nothing yet.
    public FeatureStateSnapshot? Latest => _latest;
}
