using AIUsage.Core;

namespace AIUsage.Core.Providers;

public interface IUsageProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string SignInHint { get; }
    ProviderCapabilities Capabilities { get; }
    // Ordered by fallback priority: the first collector that yields a field wins that field.
    IReadOnlyList<IUsageCollector> Collectors { get; }
}

public interface IUsageCollector
{
    string Name { get; }
    string Kind { get; }
    string Level { get; }
    int MinIntervalSeconds { get; }
    bool TokenFreeVerified { get; }
    string Verification { get; }
    // Collectors that exist in the chain but are never run automatically.
    bool Implemented { get; }
    Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken);
}

public sealed class CollectContext
{
    public required UsageProviderState Account { get; init; }
    public required string CollectionLevel { get; init; }
    public bool IncludeWsl => CollectorPolicy.LevelRank(CollectionLevel) >= CollectorPolicy.LevelRank("Deep");
}

public sealed class CollectorResult
{
    public bool Success { get; init; }
    public bool NotSignedIn { get; init; }
    public string Message { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Confidence { get; init; } = "High";
    public double? SessionPercent { get; init; }
    public DateTimeOffset? SessionResetAt { get; init; }
    public double? WeeklyPercent { get; init; }
    public DateTimeOffset? WeeklyResetAt { get; init; }
    public string? Plan { get; init; }
    public List<ModelUsage>? ModelBreakdown { get; init; }
    public ExtraUsageInfo? ExtraUsage { get; init; }
    public string? CreditsBalance { get; init; }
    // Set when the service asked us to slow down (HTTP 429): the collector is not run again before this has passed.
    public TimeSpan? RetryAfter { get; init; }

    public static CollectorResult RateLimited(string message, TimeSpan retryAfter, string detail = "") =>
        new() { Message = message, Detail = detail, RetryAfter = retryAfter };

    public static CollectorResult Fail(string message, string detail = "") => new() { Message = message, Detail = detail };
    public static CollectorResult SignedOut(string message, string detail = "") => new() { NotSignedIn = true, Message = message, Detail = detail };
}

public static class ProviderRegistry
{
    public static readonly IReadOnlyList<IUsageProvider> All = [new ClaudeProvider(), new CodexProvider(), new CopilotProvider()];

    public static IUsageProvider Get(string providerId) =>
        All.FirstOrDefault(x => x.ProviderId == providerId) ?? throw new ArgumentException($"Unknown provider {providerId}");

    public static CollectorInfo Describe(IUsageCollector collector) => new()
    {
        Name = collector.Name,
        Kind = collector.Kind,
        Level = collector.Level,
        MinIntervalSeconds = collector.MinIntervalSeconds,
        TokenFreeVerified = collector.TokenFreeVerified,
        Verification = collector.Verification,
        Status = "IDLE"
    };
}

// Last known values kept in state.json; the final fallback of every chain.
public sealed class LocalCacheCollector : IUsageCollector
{
    public string Name => "Local";
    public string Kind => "local-cache";
    public string Level => "Safe";
    public int MinIntervalSeconds => 0;
    public bool TokenFreeVerified => true;
    public string Verification => Loc.Msg("vf.local");
    public bool Implemented => true;

    public Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var account = context.Account;
        var now = DateTimeOffset.Now;
        var session = (double)account.SessionUsagePercent;
        var sessionReset = account.SessionResetAt;
        var weekly = (double)account.WeeklyUsagePercent;
        var weeklyReset = account.WeeklyResetAt;
        var estimated = false;

        // A window that has already reset is known to start again from 0% (Local Estimate).
        if (sessionReset <= now && session > 0)
        {
            session = 0;
            estimated = true;
        }
        if (weeklyReset <= now && weekly > 0)
        {
            weekly = 0;
            weeklyReset = weeklyReset.AddDays(Math.Ceiling((now - weeklyReset).TotalDays / 7) * 7);
            estimated = true;
        }

        return Task.FromResult(new CollectorResult
        {
            Success = true,
            Confidence = "Low",
            Message = estimated ? Loc.Msg("msg.localEstimate") : Loc.Msg("msg.localCache"),
            SessionPercent = session,
            SessionResetAt = sessionReset,
            WeeklyPercent = weekly,
            WeeklyResetAt = weeklyReset
        });
    }
}
