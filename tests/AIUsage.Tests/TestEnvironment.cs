using System.Runtime.CompilerServices;

namespace AIUsage.Tests;

// Keeps tests away from the real data folder and off the network.
internal static class TestEnvironment
{
    [ModuleInitializer]
    public static void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aiusage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("USAGE_MONITOR_DATA_DIR", dir);
        Environment.SetEnvironmentVariable("USAGE_MONITOR_DEMO", "1");
    }
}
