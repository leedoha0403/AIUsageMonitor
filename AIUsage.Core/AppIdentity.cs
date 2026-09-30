namespace AIUsage.Core;

// Names shared by the standalone app and Host widgets so they can tell whether the other is collecting.
public static class AppIdentity
{
    // Held by the standalone app for as long as it runs.
    public const string StandaloneMutexName = @"Local\UsageMonitorWpf-SingleInstance-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    // Held by a widget instance while it is collecting.
    public const string WidgetMutexName = @"Local\AIUsage-Widget-Collector-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    // Held by a Host widget instance for as long as it exists (docked or floating in the Host). The app refuses a
    // plain start while it is held, so the mini widget only ever lives in one place.
    public const string WidgetPresenceMutexName = @"Local\AIUsage-Widget-Presence-9F1E7B2D-6C3A-4E4A-9E1D-2E9B6D6C1A11";

    // Signalled by the app to make a Host that owns the widget show its detail window (ModuleDock's
    // WidgetAnnounce.OpenDetailEventName for this widget); the app has no reference to the Host contracts.
    public const string OpenHostDetailEventName = @"Local\ModuleDock.WidgetAnnounce.OpenDetail.dev.leedoha.aiusage.summary";

    // Asks the Host that owns the widget to show the main screen. False when no Host owns it (or does not answer),
    // so the caller shows its own.
    public static bool TryOpenHostDetail()
    {
        if (!IsHeldByAnotherProcess(WidgetPresenceMutexName)) return false;
        try
        {
            using var signal = System.Threading.EventWaitHandle.OpenExisting(OpenHostDetailEventName);
            return signal.Set();
        }
        catch (Exception ex) when (ex is System.Threading.WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
