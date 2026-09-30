namespace AIUsage.Widget;

// True while the Host is creating the widget because the app's mini widget was dropped onto it. That is the one
// moment the widget may start next to a running app: the app hides its own widget as soon as the dock succeeds.
internal static class DockArrival
{
    private static int _inProgress;

    public static bool InProgress => Volatile.Read(ref _inProgress) > 0;

    public static IDisposable Begin()
    {
        Interlocked.Increment(ref _inProgress);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref _inProgress);
        }
    }
}
