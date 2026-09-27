using System.Windows;
using UsageMonitorWpf.Core;
using UsageMonitorWpf.ViewModels;

namespace UsageMonitorWpf;

public partial class RefreshScheduleWindow : Window
{
    public RefreshScheduleWindow(ScheduleEditorViewModel editor)
    {
        InitializeComponent();
        DataContext = editor;
        editor.RequestClose += accepted =>
        {
            DialogResult = accepted;
            Close();
        };
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }
}
