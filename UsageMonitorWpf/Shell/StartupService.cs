using AIUsage.Core;
using Microsoft.Win32;

namespace UsageMonitorWpf.Shell;

// "Run at Windows sign-in" for the current user (HKCU Run key, no admin rights needed).
// The registry entry is the source of truth, so turning it off in Task Manager is reflected here too.
public static class StartupService
{
    public const string StartupArgument = "--startup";
    private const string ValueName = "UsageMonitorWpf";

    // USAGE_MONITOR_STARTUP_KEY lets tests point at a scratch key instead of the real Run key.
    private static string KeyPath =>
        Environment.GetEnvironmentVariable("USAGE_MONITOR_STARTUP_KEY") is { Length: > 0 } custom
            ? custom
            : @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string ExecutablePath => Environment.ProcessPath ?? "";

    public static bool IsEnabled => RegisteredCommand() != null;

    // Registered for a different exe (the app was moved or rebuilt elsewhere).
    public static bool IsStale => RegisteredCommand() is { } command && !command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{ExecutablePath}\" {StartupArgument}");
        else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
    }

    // Keeps an existing registration pointing at the exe that is running now.
    public static void RepairIfStale()
    {
        if (IsStale) SetEnabled(true);
    }

    private static string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }
}
