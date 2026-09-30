using AIUsage.Core;

namespace AIUsage.Presentation;

// WPF side of Loc: switches the language and exposes every key to XAML as {DynamicResource key}.
public static class LocResources
{
    public static void Apply(string language)
    {
        Loc.SetLanguage(language);
        var resources = System.Windows.Application.Current?.Resources;
        if (resources == null) return;
        foreach (var key in Loc.Keys)
        {
            resources[key] = Loc.T(key);
        }
    }
}
