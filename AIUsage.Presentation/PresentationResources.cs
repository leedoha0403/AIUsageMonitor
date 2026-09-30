using System.Windows;

namespace AIUsage.Presentation;

public static class PresentationResources
{
    private static readonly Uri BrushesUri = new("pack://application:,,,/AIUsage.Presentation;component/Brushes.xaml");

    // Views carry their own styles, but theme colors are looked up at Application level so the shell (or a Host)
    // can theme them. The defaults go in as a merged dictionary, the lowest-priority layer: any key the Host or
    // the standalone shell defines directly on Application.Resources still wins, and keys it leaves out fall back.
    public static void EnsureThemeDefaults()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        if (app.Resources.MergedDictionaries.Any(d => d.Source == BrushesUri)) return;
        app.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = BrushesUri });
    }
}
