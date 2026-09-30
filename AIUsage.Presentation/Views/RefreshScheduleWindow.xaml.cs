using System.Windows;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Presentation.Views;

// Modal editor for one account's scheduled refresh. Shown by whichever shell hosts the feature.
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
        SourceInitialized += (_, _) => TitleBar.Apply(this);
    }
}
