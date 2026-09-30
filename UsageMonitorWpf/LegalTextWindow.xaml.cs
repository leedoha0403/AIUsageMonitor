using UsageMonitorWpf.Shell;
using AIUsage.Presentation;
using System.Windows;
using AIUsage.Core;

namespace UsageMonitorWpf;

public partial class LegalTextWindow : Window
{
    public LegalTextWindow(string title, string subtitle, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        BodyText.Text = body;
        ThemeService.ApplyTitleBar(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
