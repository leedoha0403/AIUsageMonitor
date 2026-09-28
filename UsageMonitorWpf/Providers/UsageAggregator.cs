using System.Diagnostics;
using UsageMonitorWpf.Core;

namespace UsageMonitorWpf.Providers;

// Runs each account's collector chain in priority order and merges the result field by field:
// the first collector that yields a field owns it, and its source/confidence are recorded per field.
public sealed class UsageAggregator
{
    private static readonly string[] Fields = ["sessionUsagePercent", "sessionResetAt", "weeklyUsagePercent", "weeklyResetAt", "plan"];

    public async Task RefreshAsync(AppState state, bool force = false)
    {
        var accounts = state.Providers.Values.Where(a => a.Enabled).ToList();
        await Task.WhenAll(accounts.Select(a => RefreshAccountAsync(a, state.Settings.CollectionLevel, force)));
    }

    private static async Task RefreshAccountAsync(UsageProviderState account, string level, bool force)
    {
        var provider = ProviderRegistry.Get(account.ProviderId);
        var context = new CollectContext { Account = account, CollectionLevel = level };
        var filled = new HashSet<string>();
        var remoteSuccess = false;
        var signedOut = false;
        var failed = false;
        CollectorResult? detailSource = null;
        var now = DateTimeOffset.Now;
        var previousSessionPercent = account.SessionUsagePercent;

        foreach (var definition in provider.Collectors)
        {
            var info = account.Collectors.First(c => c.Name == definition.Name);
            var isLocal = definition is LocalCacheCollector;
            if (!CollectorPolicy.IsAllowed(info, level, definition.Implemented))
            {
                info.Status = "DISABLED";
                info.Message = CollectorPolicy.DisabledReason(info, level, definition.Implemented);
                info.LatencyMs = null;
                continue;
            }

            // Respect the collector's own minimum polling interval; keep the fields it produced last time.
            if (!force && !isLocal && info.LastRunAt.HasValue && now - info.LastRunAt.Value < TimeSpan.FromSeconds(definition.MinIntervalSeconds))
            {
                if (info.Status == "SUCCESS")
                {
                    foreach (var field in Fields.Where(f => account.FieldSources.TryGetValue(f, out var fs) && fs.Source == info.Name)) filled.Add(field);
                    remoteSuccess = true;
                }
                else if (info.Status == "NOT_SIGNED_IN")
                {
                    signedOut = true;
                }
                continue;
            }

            CollectorResult result;
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                result = await definition.CollectAsync(context, cts.Token);
            }
            catch (Exception ex)
            {
                result = CollectorResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
            info.LatencyMs = (int)sw.ElapsedMilliseconds;
            info.LastRunAt = now;
            info.Message = result.Message;
            info.Detail = result.Detail;
            info.Status = result.Success ? "SUCCESS" : result.NotSignedIn ? "NOT_SIGNED_IN" : "FAILED";
            if (!result.Success)
            {
                signedOut |= result.NotSignedIn;
                failed |= !result.NotSignedIn;
                continue;
            }

            info.LastSuccessAt = now;
            if (!isLocal)
            {
                remoteSuccess = true;
                detailSource ??= result;
            }
            Apply(account, result, info.Name, isLocal, filled);
        }

        var session = account.FieldSources.GetValueOrDefault("sessionUsagePercent");
        account.Source = session?.Source ?? "Manual";
        account.Confidence = session?.Confidence ?? "Low";
        account.CollectedAt = now;
        // A drop to 0% from nonzero is a window boundary we actually witnessed (manual reset, our own
        // scheduled refresh, or real usage rolling into a new window) — not the idle placeholder.
        if (previousSessionPercent > 0 && account.SessionUsagePercent == 0) account.SessionResetObservedAt = now;

        if (detailSource != null)
        {
            if (detailSource.ModelBreakdown != null) account.ModelBreakdown = detailSource.ModelBreakdown;
            if (detailSource.ExtraUsage != null) account.ExtraUsage = detailSource.ExtraUsage;
            if (detailSource.CreditsBalance != null) account.CreditsBalance = detailSource.CreditsBalance;
        }

        if (remoteSuccess)
        {
            account.Status = "READY";
            account.Message = Loc.Msg("msg.live", Loc.Msg("src." + account.Source));
            account.LastSuccessAt = detailSource != null ? now : account.LastSuccessAt;
            account.ConsecutiveFailures = 0;
        }
        else if (CollectorPolicy.LevelRank(level) == 0)
        {
            account.Status = "LOCAL";
            account.Message = Loc.Msg("msg.safeOnly");
        }
        else if (signedOut && !failed)
        {
            account.Status = "NOT_SIGNED_IN";
            account.Message = Loc.Msg("msg.notSignedIn", provider.SignInHint);
            account.ConsecutiveFailures++;
        }
        else
        {
            account.Status = "DEGRADED";
            account.Message = Loc.Msg("msg.degraded");
            account.ConsecutiveFailures++;
        }
    }

    private static void Apply(UsageProviderState account, CollectorResult result, string source, bool isLocal, HashSet<string> filled)
    {
        void Set(string field, bool hasValue, Action apply)
        {
            if (!hasValue || !filled.Add(field)) return;
            var previous = account.FieldSources.GetValueOrDefault(field);
            apply();
            // The local cache only re-serves values; keep "Manual" when the user typed them.
            var label = isLocal ? (previous?.Source == "Manual" ? "Manual" : "Local Cache") : source;
            account.FieldSources[field] = new FieldSource
            {
                Source = label,
                Confidence = result.Confidence,
                UpdatedAt = isLocal ? previous?.UpdatedAt : DateTimeOffset.Now
            };
        }

        Set("sessionUsagePercent", result.SessionPercent.HasValue, () => account.SessionUsagePercent = Clamp(result.SessionPercent!.Value));
        Set("sessionResetAt", result.SessionResetAt.HasValue, () => account.SessionResetAt = result.SessionResetAt!.Value);
        Set("weeklyUsagePercent", result.WeeklyPercent.HasValue, () => account.WeeklyUsagePercent = Clamp(result.WeeklyPercent!.Value));
        Set("weeklyResetAt", result.WeeklyResetAt.HasValue, () => account.WeeklyResetAt = result.WeeklyResetAt!.Value);
        Set("plan", !string.IsNullOrWhiteSpace(result.Plan), () => account.Plan = result.Plan!);
    }

    private static int Clamp(double value) => (int)Math.Clamp(Math.Round(value), 0, 100);
}
