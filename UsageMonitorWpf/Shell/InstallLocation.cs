using Microsoft.Win32;

namespace UsageMonitorWpf.Shell;

// Records where this app lives so a Host can start it when a widget is dragged out. Only this registered path is
// trusted by the Host; a path next to the widget DLL is not.
public static class InstallLocation
{
    public const string KeyPath = @"Software\AIUsageMonitor";
    public const string ValueName = "InstallPath";

    public static string? ExecutablePath => Environment.ProcessPath;

    public static void Register()
    {
        try
        {
            var path = ExecutablePath;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (!string.Equals(key.GetValue(ValueName) as string, path, StringComparison.OrdinalIgnoreCase)) key.SetValue(ValueName, path);
        }
        catch (Exception ex)
        {
            AIUsage.Core.AppLog.Write("could not record the install path: " + ex.Message);
        }
    }
}
