using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using AIUsage.Core;
using AIUsage.Core.Providers;
using AIUsage.Core.Storage;
using AIUsage.Presentation.ViewModels;

namespace AIUsage.Tests;

public class RateLimitTests
{
    private static HttpResponseMessage Response(RetryConditionHeaderValue? retry = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = retry;
        return response;
    }

    [Theory]
    [InlineData(120, 120)]
    [InlineData(5, 60)]      // never poll faster than once a minute
    [InlineData(99999, 1800)] // and never sit out longer than half an hour
    public void Retry_after_is_honored_within_sane_bounds(int headerSeconds, int expectedSeconds)
    {
        using var response = Response(retry: new RetryConditionHeaderValue(TimeSpan.FromSeconds(headerSeconds)));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ClaudeOAuthUsageCollector.RetryDelay(response));
    }

    [Fact]
    public void Without_a_retry_after_header_the_endpoint_is_left_alone_for_a_few_minutes()
    {
        using var response = Response();
        Assert.Equal(TimeSpan.FromMinutes(5), ClaudeOAuthUsageCollector.RetryDelay(response));
    }

    [Fact]
    public async Task A_rate_limited_collector_is_not_run_again_before_it_may_be()
    {
        var state = Defaults.CreateState();
        foreach (var key in state.Providers.Keys.Where(k => k != "claude").ToList()) state.Providers.Remove(key);
        var claude = state.Providers["claude"];
        var official = claude.Collectors.First(c => c.Name == "Official");
        var lastRun = DateTimeOffset.Now.AddMinutes(-2);
        official.Status = "FAILED";
        official.LastRunAt = lastRun;
        official.BackoffUntil = DateTimeOffset.Now.AddMinutes(10);
        claude.SessionUsagePercent = 84;

        await new UsageAggregator().RefreshAsync(state);

        Assert.Equal(lastRun, official.LastRunAt); // not asked again
        Assert.Equal(84, claude.SessionUsagePercent); // the last known number is kept
    }

    [Fact]
    public async Task Adopting_an_older_copy_does_not_send_the_numbers_back_in_time()
    {
        var vm = await WpfHost.Call(() => Task.FromResult(new UsageFeatureViewModel(
            new StateStore(Path.Combine(Path.GetTempPath(), "aiusage-stale-" + Guid.NewGuid().ToString("N"))), new FakeUi())));

        var fresh = vm.State.Providers["claude"];
        fresh.SessionUsagePercent = 84;
        fresh.LastSuccessAt = DateTimeOffset.Now;

        var staleState = Defaults.CreateState();
        var stale = staleState.Providers["claude"];
        stale.SessionUsagePercent = 79;
        stale.LastSuccessAt = DateTimeOffset.Now.AddMinutes(-8);
        stale.AccountName = "Renamed on the other side";

        await WpfHost.Run(() => vm.AdoptFeatureState(FeatureStateSnapshot.Capture(staleState)));

        var adopted = vm.State.Providers["claude"];
        Assert.Equal(84, adopted.SessionUsagePercent);
        Assert.Equal("Renamed on the other side", adopted.AccountName); // settings still travel
    }
}
