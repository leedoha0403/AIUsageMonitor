namespace AIUsage.Presentation.ViewModels;

// What the feature may ask the host for. Mirrors the capabilities the widget manifest declares.
public enum UsageCapability
{
    FileSystem,
    Network,
    ProcessExecution
}

// Everything the feature view models need from the surrounding UI, so they never create windows or dialogs
// themselves. The standalone app and the Host widget adapter each provide their own implementation.
public interface IUiServices
{
    // OK/Cancel question; true when the user confirms.
    bool Confirm(string title, string message);

    // Folder picker; null when cancelled.
    string? PickFolder(string description, string initialDirectory);

    // Shows the scheduled-refresh editor modally; true when the user closed it with a result to apply.
    bool EditSchedule(ScheduleEditorViewModel editor);

    // Runs the action on the UI thread later.
    void Post(Action action);

    // True when the host/user has granted the capability. The standalone app grants everything.
    bool IsGranted(UsageCapability capability);

    // Asks the host to grant the capability (it may prompt the user).
    Task<bool> RequestAsync(UsageCapability capability);
}
