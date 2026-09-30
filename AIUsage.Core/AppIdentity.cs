namespace AIUsage.Core;

// Names shared by the standalone app and Host widgets so they can tell whether the other is collecting.
public static class AppIdentity
{
    // Held by the standalone app for as long as it runs.
    public const string StandaloneMutexName = @"Local\UsageMonitorWpf-SingleInstance-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    // Held by a widget instance while it is collecting.
    public const string WidgetMutexName = @"Local\AIUsage-Widget-Collector-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    public static bool IsHeldByAnotherProcess(string mutexName)
    {
        try
        {
            if (!System.Threading.Mutex.TryOpenExisting(mutexName, out var existing)) return false;
            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
