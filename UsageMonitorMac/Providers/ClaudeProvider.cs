using System.Diagnostics;
using System.Net;
using System.Text.Json;
using UsageMonitorMac.Core;

namespace UsageMonitorMac.Providers;

public sealed class ClaudeProvider : IUsageProvider
{
    public string ProviderId => "claude";
    public string DisplayName => "Claude Code";
    public string SignInHint => "Run `claude auth login`.";
    public ProviderCapabilities Capabilities { get; } = new() { MultiAccount = true, ModelBreakdown = true, ExtraUsage = true };
    public IReadOnlyList<IUsageCollector> Collectors { get; } =
    [
        new ClaudeOAuthUsageCollector(),
        new ClaudeRateLimitHeaderCollector(),
        new LocalCacheCollector()
    ];
}

public sealed class ClaudeOAuthUsageCollector : IUsageCollector
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    public string Name => "Official";
    public string Kind => "oauth-usage-endpoint";
    public string Level => "Standard";
    public int MinIntervalSeconds => 60;
    public bool TokenFreeVerified => true;
    public string Verification => "GET /api/oauth/usage is a status lookup.";
    public bool Implemented => true;

    public async Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var candidates = CredentialLocator.Candidates(context.Account.ConfigDirectory, "CLAUDE_CONFIG_DIR", ".claude", ".credentials.json", context.IncludeDeepLocalSearch);
        var found = CredentialLocator.FindFirst(candidates);
        if (found == null) return CollectorResult.SignedOut("Claude login not found", $"Searched: {string.Join(", ", candidates.Select(c => c.Label))}");

        string token;
        string? plan = null;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(found.Path, cancellationToken));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var tokenElement) ||
                string.IsNullOrEmpty(tokenElement.GetString()))
            {
                return CollectorResult.SignedOut("Claude OAuth token not found", $"Credential: {found.Label}");
            }
            token = tokenElement.GetString()!;
            if (oauth.TryGetProperty("subscriptionType", out var sub) && sub.ValueKind == JsonValueKind.String) plan = sub.GetString();
            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.TryGetInt64(out var expMs) &&
                DateTimeOffset.FromUnixTimeMilliseconds(expMs) <= DateTimeOffset.Now)
            {
                return CollectorResult.SignedOut("Claude login expired", $"Credential: {found.Label}");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CollectorResult.Fail("Could not read Claude credential", $"{found.Label}: {ex.GetType().Name}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var response = await Http.Client.SendAsync(request, cancellationToken);
        var detail = $"Credential: {found.Label} · HTTP {(int)response.StatusCode}";
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return CollectorResult.SignedOut("Claude rejected the saved login", detail);
        if (!response.IsSuccessStatusCode) return CollectorResult.Fail($"HTTP {(int)response.StatusCode}", detail);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        var five = Window(root, "five_hour");
        var week = Window(root, "seven_day");
        var models = new List<ModelUsage>();
        foreach (var (key, label) in new[] { ("seven_day_opus", "Opus (7d)"), ("seven_day_sonnet", "Sonnet (7d)"), ("seven_day_oauth_apps", "OAuth apps (7d)") })
        {
            var w = Window(root, key);
            if (w.Percent.HasValue) models.Add(new ModelUsage { Name = label, Percent = (int)Math.Round(w.Percent.Value), ResetAt = w.ResetAt });
        }

        ExtraUsageInfo? extra = null;
        if (root.TryGetProperty("extra_usage", out var ex2) && ex2.ValueKind == JsonValueKind.Object)
        {
            extra = new ExtraUsageInfo
            {
                IsEnabled = ex2.TryGetProperty("is_enabled", out var en) && en.ValueKind == JsonValueKind.True,
                UsedDollars = Number(ex2, "used_credits") is { } used ? (decimal)used / 100m : null,
                MonthlyLimitDollars = Number(ex2, "monthly_limit") is { } limit ? (decimal)limit / 100m : null,
                UtilizationPercent = Number(ex2, "utilization") is { } u ? (int)Math.Round(u) : null
            };
        }

        return new CollectorResult
        {
            Success = true,
            Confidence = "High",
            Message = "Official usage endpoint",
            Detail = detail,
            SessionPercent = five.Percent,
            SessionResetAt = five.ResetAt,
            WeeklyPercent = week.Percent,
            WeeklyResetAt = week.ResetAt,
            Plan = plan,
            ModelBreakdown = models,
            ExtraUsage = extra
        };
    }

    private static (double? Percent, DateTimeOffset? ResetAt) Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(r.GetString(), out var parsed)) reset = parsed;
        return (Number(w, "utilization"), reset);
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

public sealed class ClaudeRateLimitHeaderCollector : IUsageCollector
{
    public string Name => "RateLimit";
    public string Kind => "messages-rate-limit-headers";
    public string Level => "Deep";
    public int MinIntervalSeconds => 300;
    public bool TokenFreeVerified => false;
    public string Verification => "Disabled because Messages API calls consume usage.";
    public bool Implemented => false;
    public Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken) =>
        Task.FromResult(CollectorResult.Fail("Rate-limit header collector is disabled"));
}

internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UsageMonitorMac/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public static async Task<(T Result, int Ms)> Time<T>(Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var result = await action();
        return (result, (int)sw.ElapsedMilliseconds);
    }
}
