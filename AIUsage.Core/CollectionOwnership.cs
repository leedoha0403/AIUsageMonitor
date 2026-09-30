namespace AIUsage.Core;

// "One collector at a time": the standalone app always wins; among widget processes the first to take the
// widget mutex wins. Whoever does not own collection keeps showing data but must not poll, notify or write.
// Mutexes are thread-affine, so call Evaluate/Dispose from one thread (the UI thread).
public sealed class CollectionOwnership : IDisposable
{
    private readonly string _standaloneName;
    private readonly string _widgetName;
    private readonly bool _isStandalone;
    private Mutex? _owned;

    // isStandalone: the standalone app itself (it holds the standalone mutex elsewhere and always collects).
    public CollectionOwnership(bool isStandalone,
        string standaloneMutexName = AppIdentity.StandaloneMutexName,
        string widgetMutexName = AppIdentity.WidgetMutexName)
    {
        _isStandalone = isStandalone;
        _standaloneName = standaloneMutexName;
        _widgetName = widgetMutexName;
    }

    public bool MayCollect { get; private set; }

    // Re-checks who is running; returns true when MayCollect changed.
    public bool Evaluate()
    {
        var may = _isStandalone || Decide();
        var changed = may != MayCollect;
        MayCollect = may;
        return changed;
    }

    private bool Decide()
    {
        if (AppIdentity.IsHeldByAnotherProcess(_standaloneName))
        {
            Release();
            return false;
        }
        if (_owned != null) return true;
        try
        {
            var mutex = new Mutex(true, _widgetName, out var created);
            if (created)
            {
                _owned = mutex;
                return true;
            }
            mutex.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Release()
    {
        if (_owned == null) return;
        try
        {
            _owned.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _owned.Dispose();
        _owned = null;
    }

    public void Dispose()
    {
        Release();
        MayCollect = false;
    }
}
