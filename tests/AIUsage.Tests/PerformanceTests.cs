using System.Reflection;
using AIUsage.Core;
using AIUsage.Core.Providers;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Tests;

// Guards for the work-avoidance changes: what the once-a-second tick refreshes, and the Codex session-log tail read.
public class PerformanceTests
{
    // The tick raises ProviderViewModel.TimeDependentProperties only. Any other property whose value moves with the
    // clock alone would stop updating on screen, so this reads every property, lets time pass, and compares.
    [Fact]
    public async Task The_clock_only_changes_properties_the_tick_refreshes()
    {
        var account = Defaults.CreateState().Providers.Values.First(p => p.ProviderId == "claude");
        account.Status = "READY";
        account.Enabled = true;
        account.SessionUsagePercent = 30;
        account.WeeklyUsagePercent = 20;
        account.SessionResetAt = DateTimeOffset.Now.AddSeconds(1.2);
        account.WeeklyResetAt = DateTimeOffset.Now.AddSeconds(1.2);
        account.LastSuccessAt = DateTimeOffset.Now.AddSeconds(-5);
        account.Refresh.Enabled = true;
        account.Refresh.Status = "Scheduled";
        account.Refresh.ScheduledFor = DateTimeOffset.Now.AddMinutes(5);
        var vm = new ProviderViewModel(account, 0);

        var readable = typeof(ProviderViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod != null && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(int)))
            .ToList();

        Dictionary<string, object?> Snapshot() => readable.ToDictionary(p => p.Name, p =>
        {
            try { return p.GetValue(vm); }
            catch (TargetInvocationException) { return null; }
        });

        var before = Snapshot();
        await Task.Delay(1500);
        var after = Snapshot();

        var changed = readable.Select(p => p.Name).Where(n => !Equals(before[n], after[n])).ToList();
        Assert.Contains(nameof(ProviderViewModel.Countdown), changed);
        var untracked = changed.Except(ProviderViewModel.TimeDependentProperties).ToList();
        Assert.True(untracked.Count == 0, "changed with the clock but not refreshed by the tick: " + string.Join(", ", untracked));
    }

    private static string RateLimitLine(double usedPercent, DateTimeOffset at) =>
        "{\"timestamp\":\"" + at.UtcDateTime.ToString("o") + "\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":" +
        usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"resets_at\":" + DateTimeOffset.Now.AddHours(3).ToUnixTimeSeconds() +
        "},\"secondary\":{\"used_percent\":10.0,\"resets_at\":" + DateTimeOffset.Now.AddDays(3).ToUnixTimeSeconds() + "}}}}";

    private static async Task<CollectorResult> CollectCodex(string home)
    {
        var account = Defaults.CreateState().Providers.Values.First(p => p.ProviderId == "codex");
        account.ConfigDirectory = home;
        return await new CodexSessionLogCollector().CollectAsync(new CollectContext { Account = account, CollectionLevel = "Deep" }, CancellationToken.None);
    }

    private static (string Home, string File) NewSessionLog()
    {
        var home = Path.Combine(Path.GetTempPath(), "codex-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(home, "sessions", "2026", "10", "02");
        Directory.CreateDirectory(dir);
        return (home, Path.Combine(dir, "rollout.jsonl"));
    }

    [Fact]
    public async Task The_newest_rate_limit_event_is_found_far_from_the_end_of_a_large_log()
    {
        var (home, file) = NewSessionLog();
        // The only event sits before ~1.5 MB of other lines, far outside the first 256 KB tail window.
        var filler = new string('x', 900);
        await File.WriteAllLinesAsync(file, new[] { RateLimitLine(42, DateTimeOffset.Now.AddMinutes(-3)) }.Concat(Enumerable.Repeat("{\"payload\":\"" + filler + "\"}", 1700)));

        var result = await CollectCodex(home);

        Assert.True(result.Success);
        Assert.Equal(42, result.SessionPercent);
    }

    [Fact]
    public async Task A_line_longer_than_the_first_window_is_read_whole()
    {
        var (home, file) = NewSessionLog();
        var big = RateLimitLine(55, DateTimeOffset.Now.AddMinutes(-1)).Replace("\"payload\":{", "\"note\":\"" + new string('y', 600_000) + "\",\"payload\":{");
        await File.WriteAllLinesAsync(file, ["{\"a\":1}", big]);

        var result = await CollectCodex(home);

        Assert.True(result.Success);
        Assert.Equal(55, result.SessionPercent);
    }

    [Fact]
    public async Task A_later_event_replaces_the_cached_one_when_the_log_grows()
    {
        var (home, file) = NewSessionLog();
        await File.WriteAllLinesAsync(file, [RateLimitLine(10, DateTimeOffset.Now.AddMinutes(-5))]);
        Assert.Equal(10, (await CollectCodex(home)).SessionPercent);

        await File.AppendAllLinesAsync(file, ["{\"other\":true}", RateLimitLine(77, DateTimeOffset.Now)]);

        Assert.Equal(77, (await CollectCodex(home)).SessionPercent);
    }

    [Fact]
    public async Task A_log_with_no_rate_limit_event_reports_none()
    {
        var (home, file) = NewSessionLog();
        await File.WriteAllLinesAsync(file, Enumerable.Repeat("{\"payload\":\"nothing here\"}", 50));

        var result = await CollectCodex(home);

        Assert.False(result.Success);
    }
}
