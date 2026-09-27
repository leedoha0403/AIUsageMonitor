using UsageMonitorMac.Core;

namespace UsageMonitorMac.Providers;

public sealed class UsageAggregator
{
    public async Task RefreshAsync(AppState state, CancellationToken cancellationToken)
    {
        foreach (var account in state.Providers.Values.Where(p => p.Enabled))
        {
            await RefreshAccountAsync(account, state.Settings.CollectionLevel, cancellationToken);
        }
        state.UpdatedAt = DateTimeOffset.Now;
    }

    private static async Task RefreshAccountAsync(UsageProviderState account, string collectionLevel, CancellationToken cancellationToken)
    {
        Defaults.SyncDefinition(account);
        var provider = ProviderRegistry.Get(account.ProviderId);
        var context = new CollectContext { Account = account, CollectionLevel = collectionLevel };
        var fieldOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anySuccess = false;
        var signedOut = false;
        var messages = new List<string>();

        foreach (var collector in provider.Collectors)
        {
            var info = account.Collectors.First(c => c.Name.Equals(collector.Name, StringComparison.OrdinalIgnoreCase));
            if (!collector.Implemented || CollectorPolicy.LevelRank(collector.Level) > CollectorPolicy.LevelRank(collectionLevel))
            {
                info.Status = collector.Implemented ? "SKIPPED" : "DISABLED";
                continue;
            }

            info.LastRunAt = DateTimeOffset.Now;
            try
            {
                var (result, ms) = await Http.Time(() => collector.CollectAsync(context, cancellationToken));
                info.LatencyMs = ms;
                info.Message = result.Message;
                info.Detail = result.Detail;
                info.Status = result.Success ? "OK" : result.NotSignedIn ? "NOT_SIGNED_IN" : "FAILED";
                if (result.Success) info.LastSuccessAt = DateTimeOffset.Now;
                ApplyResult(account, collector.Name, result, fieldOwners);
                anySuccess |= result.Success;
                signedOut |= result.NotSignedIn;
                if (!string.IsNullOrWhiteSpace(result.Message)) messages.Add($"{collector.Name}: {result.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                info.Status = "FAILED";
                info.Message = ex.GetType().Name;
                info.Detail = ex.Message;
                messages.Add($"{collector.Name}: {ex.Message}");
            }
        }

        account.CollectedAt = DateTimeOffset.Now;
        account.Status = anySuccess ? "READY" : signedOut ? "NOT_SIGNED_IN" : "ERROR";
        account.Message = messages.FirstOrDefault() ?? "";
        if (anySuccess)
        {
            account.ConsecutiveFailures = 0;
            account.LastSuccessAt = DateTimeOffset.Now;
        }
        else
        {
            account.ConsecutiveFailures++;
        }
    }

    private static void ApplyResult(UsageProviderState account, string source, CollectorResult result, HashSet<string> owners)
    {
        if (!result.Success) return;
        account.Source = source;
        account.Confidence = result.Confidence;

        if (result.SessionPercent.HasValue && owners.Add("sessionUsagePercent"))
        {
            account.SessionUsagePercent = ClampPercent(result.SessionPercent.Value);
            account.FieldSources["sessionUsagePercent"] = Field(source, result.Confidence);
        }
        if (result.SessionResetAt.HasValue && owners.Add("sessionResetAt"))
        {
            account.SessionResetAt = result.SessionResetAt.Value;
            account.FieldSources["sessionResetAt"] = Field(source, result.Confidence);
        }
        if (result.WeeklyPercent.HasValue && owners.Add("weeklyUsagePercent"))
        {
            account.WeeklyUsagePercent = ClampPercent(result.WeeklyPercent.Value);
            account.FieldSources["weeklyUsagePercent"] = Field(source, result.Confidence);
        }
        if (result.WeeklyResetAt.HasValue && owners.Add("weeklyResetAt"))
        {
            account.WeeklyResetAt = result.WeeklyResetAt.Value;
            account.FieldSources["weeklyResetAt"] = Field(source, result.Confidence);
        }
        if (!string.IsNullOrWhiteSpace(result.Plan) && owners.Add("plan"))
        {
            account.Plan = result.Plan;
            account.FieldSources["plan"] = Field(source, result.Confidence);
        }

        if (result.ModelBreakdown != null) account.ModelBreakdown = result.ModelBreakdown;
        if (result.ExtraUsage != null) account.ExtraUsage = result.ExtraUsage;
        if (result.CreditsBalance != null) account.CreditsBalance = result.CreditsBalance;
    }

    private static FieldSource Field(string source, string confidence) => new()
    {
        Source = source,
        Confidence = confidence,
        UpdatedAt = DateTimeOffset.Now
    };

    private static int ClampPercent(double value) => Math.Clamp((int)Math.Round(value), 0, 100);
}
