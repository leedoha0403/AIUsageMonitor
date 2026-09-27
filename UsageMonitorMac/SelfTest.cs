using UsageMonitorMac.Core;
using UsageMonitorMac.Refresh;
using UsageMonitorMac.Storage;

namespace UsageMonitorMac;

internal static class SelfTest
{
    public static int Run()
    {
        var temp = Path.Combine(Path.GetTempPath(), "UsageMonitorMacTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousDataDir = Environment.GetEnvironmentVariable("USAGE_MONITOR_DATA_DIR");
        var previousDryRun = Environment.GetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN");

        try
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_DATA_DIR", temp);
            Environment.SetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN", "ok");

            var state = Defaults.CreateState();
            Assert(state.Providers.ContainsKey("claude"), "default claude account");
            Assert(state.Providers.ContainsKey("codex"), "default codex account");

            var store = new StateStore("UsageMonitorMacTest");
            store.SaveState(state);
            var loaded = store.LoadState();
            Assert(loaded.Providers.Count == 2, "state round trip");
            Assert(store.AppendHistory(loaded, force: true), "history append");
            Assert(store.LoadHistory().Count == 2, "history read");

            var sanitized = RefreshRunner.SanitizePrompt(" hi\r\nthere with a very very very very very long suffix ");
            Assert(!sanitized.Contains('\n'), "prompt strips control chars");
            Assert(sanitized.Length <= 40, "prompt limit");

            var runner = new RefreshRunner(store.DataDirectory);
            var refresh = runner.RunAsync(loaded.Providers["codex"], CancellationToken.None).GetAwaiter().GetResult();
            Assert(refresh.Success, "refresh dry-run");

            Console.WriteLine("UsageMonitorMac self-test OK");
            Console.WriteLine($"Test data: {temp}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UsageMonitorMac self-test FAILED: {ex.Message}");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_DATA_DIR", previousDataDir);
            Environment.SetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN", previousDryRun);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
