using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AIUsage.Core;

namespace AIUsage.Core.Providers;

public sealed class ClaudeProvider : IUsageProvider
{
    public string ProviderId => "claude";
    public string DisplayName => "Claude Code";
    public string SignInHint => Loc.Msg("hint.claude");
    public ProviderCapabilities Capabilities { get; } = new() { MultiAccount = true, WslCredentialDetection = true, ModelBreakdown = true, ExtraUsage = true };
    public IReadOnlyList<IUsageCollector> Collectors { get; } =
    [
        new ClaudeOAuthUsageCollector(),
        new ClaudeRateLimitHeaderCollector(),
        new LocalCacheCollector()
    ];
}

// Reuses the Claude Code CLI login (read-only) and calls the same usage endpoint the CLI's /usage uses.
// It is a status lookup, not a model call, so it does not consume usage.
public sealed class ClaudeOAuthUsageCollector : IUsageCollector
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    public string Name => "Official";
    public string Kind => "oauth-usage-endpoint";
    public string Level => "Standard";
    public int MinIntervalSeconds => 60;
    public bool TokenFreeVerified => true;
    public string Verification => Loc.Msg("vf.claudeOfficial");
    public bool Implemented => true;

    public async Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var candidates = CredentialLocator.Candidates(context.Account.ConfigDirectory, "CLAUDE_CONFIG_DIR", ".claude", ".credentials.json", context.IncludeWsl);
        var found = CredentialLocator.FindFirst(candidates);
        if (found == null)
        {
            return CollectorResult.SignedOut(Loc.Msg("msg.claudeNoLogin"),
                Loc.Msg("msg.searched", string.Join(", ", candidates.Select(c => c.Label))));
        }

        string token;
        string? plan = null;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(found.Path, cancellationToken));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var tokenElement) ||
                string.IsNullOrEmpty(tokenElement.GetString()))
            {
                return CollectorResult.SignedOut(Loc.Msg("msg.claudeNoOauth"), Loc.Msg("msg.credential", found.Label));
            }
            token = tokenElement.GetString()!;
            if (oauth.TryGetProperty("subscriptionType", out var sub) && sub.ValueKind == JsonValueKind.String) plan = sub.GetString();
            // Do not pre-judge by expiresAt: the CLI refreshes this access token in the background well
            // before it is actually rejected, so a locally-computed "expired" timestamp is often stale by
            // the time we read it and would falsely report "signed out" on a perfectly valid login. Let the
            // server's real 401/403 response below decide instead.
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CollectorResult.Fail(Loc.Msg("msg.readCredFail", "Claude"), $"{found.Label}: {ex.GetType().Name}");
        }

        var response = await SendAsync(token, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The access token is short-lived and only the CLI refreshes it. Ask the CLI to do so, then retry once.
            if (found.Label != "WSL" && await LoginHelper.TryRefreshClaudeAsync(context.Account.ConfigDirectory, cancellationToken) &&
                await ReadAccessTokenAsync(found.Path, cancellationToken) is { } fresh && fresh != token)
            {
                response.Dispose();
                response = await SendAsync(fresh, cancellationToken);
            }
        }
        using var _ = response;
        var detail = Loc.Msg("msg.credential", $"{found.Label} · HTTP {(int)response.StatusCode}");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return CollectorResult.SignedOut(Loc.Msg("msg.claudeRejected"), detail);
        }
        if (!response.IsSuccessStatusCode) return CollectorResult.Fail(Loc.Msg("msg.httpError", (int)response.StatusCode), detail);

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
            // Credit amounts are reported in cents.
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
            Message = Loc.Msg("msg.official"),
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

    private static Task<HttpResponseMessage> SendAsync(string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return Http.Client.SendAsync(request, cancellationToken);
    }

    private static async Task<string?> ReadAccessTokenAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
            return doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) && oauth.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
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

// Kept in the chain for diagnostics only: reading rate-limit headers needs a Messages API call, which consumes usage.
public sealed class ClaudeRateLimitHeaderCollector : IUsageCollector
{
    public string Name => "RateLimit";
    public string Kind => "messages-rate-limit-headers";
    public string Level => "Deep";
    public int MinIntervalSeconds => 300;
    public bool TokenFreeVerified => false;
    public string Verification => Loc.Msg("vf.rateLimit");
    public bool Implemented => false;

    public Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken) =>
        Task.FromResult(CollectorResult.Fail(Loc.Msg("msg.rateLimitDisabled")));
}

internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UsageMonitorWpf/0.3");
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
