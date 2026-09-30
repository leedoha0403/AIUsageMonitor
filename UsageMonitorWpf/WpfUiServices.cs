using System.Windows;
using AIUsage.Presentation.ViewModels;

namespace UsageMonitorWpf;

// Dialogs of the standalone app, owned by the dashboard window while it is visible.
public sealed class WpfUiServices : IUiServices
{
    private static Window? Owner =>
        System.Windows.Application.Current.MainWindow is { IsVisible: true } main ? main : null;

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
        new AIUsage.Presentation.Views.RefreshScheduleWindow(editor) { Owner = Owner }.ShowDialog() == true;

    public bool IsGranted(UsageCapability capability) => true;

    public Task<bool> RequestAsync(UsageCapability capability) => Task.FromResult(true);

    public void Post(Action action) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action);
}
