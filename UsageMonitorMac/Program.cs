using UsageMonitorMac.Core;
using UsageMonitorMac.Providers;
using UsageMonitorMac.Refresh;
using UsageMonitorMac.Storage;
using UsageMonitorMac.Web;

namespace UsageMonitorMac;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        var store = new StateStore("UsageMonitorMac");
        var state = store.LoadState();
        var aggregator = new UsageAggregator();

        if (args.Contains("--ui", StringComparer.OrdinalIgnoreCase) || args.Contains("--serve", StringComparer.OrdinalIgnoreCase))
        {
            var port = ReadPort(args);
            var server = new LocalDashboardServer(store, aggregator, port);
            await server.RunAsync(CancellationToken.None);
            return 0;
        }

        if (args.Contains("--refresh-dry-run", StringComparer.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("USAGE_MONITOR_REFRESH_DRYRUN", "ok");
            await aggregator.RefreshAsync(state, CancellationToken.None);
            var account = SelectAccount(args, state);
            var runner = new RefreshRunner(store.DataDirectory);
            var result = await runner.RunAsync(account, CancellationToken.None);
            Console.WriteLine(result.Success
                ? $"Refresh dry-run OK: {result.CommandLine}"
                : $"Refresh dry-run failed: {result.ErrorKind} {result.Message}");
            return result.Success ? 0 : 2;
        }

        await aggregator.RefreshAsync(state, CancellationToken.None);
        store.SaveState(state);
        store.AppendHistory(state);

        PrintSummary(state, store);
        if (!args.Contains("--once", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine("This is the mac-compatible core preview. Run with --once for scripts, --self-test for smoke tests.");
        }
        return 0;
    }

    private static int ReadPort(string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--port", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var port) ? port : 5279;
    }

    private static UsageProviderState SelectAccount(string[] args, AppState state)
    {
        var provider = "codex";
        var index = Array.FindIndex(args, a => a.Equals("--provider", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length) provider = args[index + 1].Trim().ToLowerInvariant();
        return state.Providers.Values.FirstOrDefault(p => p.ProviderId == provider) ?? state.Providers.Values.First();
    }

    private static void PrintSummary(AppState state, StateStore store)
    {
        Console.WriteLine("Usage Monitor Mac");
        Console.WriteLine($"Data: {store.DataDirectory}");
        Console.WriteLine($"Updated: {state.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        foreach (var account in state.Providers.Values.Where(p => p.Enabled).OrderBy(p => p.ProviderId))
        {
            var session = $"{account.SessionUsagePercent,3}% used, resets {Formatters.ShortCountdown(account.SessionResetAt)}";
            var weekly = $"{account.WeeklyUsagePercent,3}% used, resets {Formatters.ShortCountdown(account.WeeklyResetAt)}";
            Console.WriteLine($"{account.DisplayName} / {account.AccountName}");
            Console.WriteLine($"  5H:    {session}");
            Console.WriteLine($"  Week:  {weekly}");
            Console.WriteLine($"  State: {account.Status} · {account.Source} · {account.Confidence}");
            if (!string.IsNullOrWhiteSpace(account.Message)) Console.WriteLine($"  Note:  {account.Message}");
            Console.WriteLine();
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        UsageMonitorMac

          dotnet run --project UsageMonitorMac -- --once
          dotnet run --project UsageMonitorMac -- --ui
          dotnet run --project UsageMonitorMac -- --self-test
          dotnet run --project UsageMonitorMac -- --refresh-dry-run --provider codex

        Environment:
          USAGE_MONITOR_DATA_DIR        Use a test data directory.
          USAGE_MONITOR_REFRESH_DRYRUN  ok|fail, skips real CLI refresh calls.
        """);
    }
}
