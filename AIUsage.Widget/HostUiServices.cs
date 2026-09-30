using System.Windows;
using System.Windows.Threading;
using AIUsage.Presentation.ViewModels;
using AIUsage.Presentation.Views;
using Dora.Widget.Abstractions;

namespace AIUsage.Widget;

// Dialogs and permission checks for a widget running inside the Host. Sensitive actions go through the Host's
// permission service; nothing here reaches into the Host's windows beyond using its main window as dialog owner.
public sealed class HostUiServices : IUiServices
{
    private readonly IWidgetPermissionService _permissions;
    private readonly Dispatcher _dispatcher;

    // Must be created on the UI thread.
    public HostUiServices(IWidgetPermissionService permissions)
    {
        _permissions = permissions;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    private static Window? Owner =>
        System.Windows.Application.Current?.MainWindow is { IsVisible: true } main ? main : null;

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? PickFolder(string description, string initialDirectory)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = initialDirectory
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public bool EditSchedule(ScheduleEditorViewModel editor) =>
        new RefreshScheduleWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public bool IsGranted(UsageCapability capability) => _permissions.IsGranted(Map(capability));

    public Task<bool> RequestAsync(UsageCapability capability) => _permissions.RequestAsync(Map(capability));

    public static WidgetCapabilities Map(UsageCapability capability) => capability switch
    {
        UsageCapability.FileSystem => WidgetCapabilities.FileSystem,
        UsageCapability.Network => WidgetCapabilities.Network,
        UsageCapability.ProcessExecution => WidgetCapabilities.ProcessExecution,
        _ => WidgetCapabilities.None
    };
}
