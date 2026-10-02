using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AIUsage.Core;

namespace AIUsage.Core.Providers;

public sealed class CodexProvider : IUsageProvider
{
    public string ProviderId => "codex";
    public string DisplayName => "Codex";
    public string SignInHint => Loc.Msg("hint.codex");
    public ProviderCapabilities Capabilities { get; } = new() { Credits = true, MultiAccount = true, WslCredentialDetection = true };
    public IReadOnlyList<IUsageCollector> Collectors { get; } =
    [
        new CodexUsageEndpointCollector(),
        new CodexSessionLogCollector(),
        new LocalCacheCollector()
    ];

    public static string CodexHome(UsageProviderState account)
    {
        if (!string.IsNullOrWhiteSpace(account.ConfigDirectory)) return Environment.ExpandEnvironmentVariables(account.ConfigDirectory);
        var env = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }
}

// Reuses the Codex CLI ChatGPT login (read-only) against the usage endpoint behind Codex's /status.
public sealed class CodexUsageEndpointCollector : IUsageCollector
{
    private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    public string Name => "Official";
    public string Kind => "chatgpt-usage-endpoint";
    public string Level => "Standard";
    public int MinIntervalSeconds => 60;
    public bool TokenFreeVerified => true;
    public string Verification => Loc.Msg("vf.codexOfficial");
    public bool Implemented => true;

    public async Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var candidates = CredentialLocator.Candidates(context.Account.ConfigDirectory, "CODEX_HOME", ".codex", "auth.json", context.IncludeWsl);
        var found = CredentialLocator.FindFirst(candidates);
        if (found == null)
        {
            return CollectorResult.SignedOut(Loc.Msg("msg.codexNoLogin"), Loc.Msg("msg.searched", string.Join(", ", candidates.Select(c => c.Label))));
        }

        string token;
        string? accountId = null;
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(found.Path, cancellationToken));
            var root = doc.RootElement;
            if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object ||
                !tokens.TryGetProperty("access_token", out var access) || string.IsNullOrEmpty(access.GetString()))
            {
                return CollectorResult.SignedOut(Loc.Msg("msg.codexApiKey"), Loc.Msg("msg.credential", found.Label));
            }
            token = access.GetString()!;
            if (tokens.TryGetProperty("account_id", out var acc) && acc.ValueKind == JsonValueKind.String) accountId = acc.GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CollectorResult.Fail(Loc.Msg("msg.readCredFail", "Codex"), $"{found.Label}: {ex.GetType().Name}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(accountId)) request.Headers.Add("ChatGPT-Account-Id", accountId);
        using var response = await Http.Client.SendAsync(request, cancellationToken);
        var detail = Loc.Msg("msg.credential", $"{found.Label} · HTTP {(int)response.StatusCode}");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return CollectorResult.SignedOut(Loc.Msg("msg.codexRejected"), detail);
        }
        if (!response.IsSuccessStatusCode) return CollectorResult.Fail(Loc.Msg("msg.httpError", (int)response.StatusCode), detail);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var rootBody = body.RootElement;
        var limits = rootBody.TryGetProperty("rate_limit", out var rl) && rl.ValueKind == JsonValueKind.Object ? rl : default;
        var primary = Window(limits, "primary_window");
        var secondary = Window(limits, "secondary_window");
        string? plan = rootBody.TryGetProperty("plan_type", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        return new CollectorResult
        {
            Success = primary.Percent.HasValue || secondary.Percent.HasValue,
            Confidence = "High",
            Message = primary.Percent.HasValue || secondary.Percent.HasValue ? Loc.Msg("msg.official") : Loc.Msg("msg.noWindows"),
            Detail = detail,
            SessionPercent = primary.Percent,
            SessionResetAt = primary.ResetAt,
            WeeklyPercent = secondary.Percent,
            WeeklyResetAt = secondary.ResetAt,
            Plan = plan,
            CreditsBalance = Credits(rootBody)
        };
    }

    private static (double? Percent, DateTimeOffset? ResetAt) Window(JsonElement limits, string name)
    {
        if (limits.ValueKind != JsonValueKind.Object || !limits.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
        double? percent = w.TryGetProperty("used_percent", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("reset_at", out var r) && r.ValueKind == JsonValueKind.Number) reset = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).ToLocalTime();
        else if (w.TryGetProperty("reset_after_seconds", out var ra) && ra.ValueKind == JsonValueKind.Number) reset = DateTimeOffset.Now.AddSeconds(ra.GetDouble());
        return (percent, reset);
    }

    internal static string? Credits(JsonElement root)
    {
        if (!root.TryGetProperty("credits", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        if (c.TryGetProperty("unlimited", out var un) && un.ValueKind == JsonValueKind.True) return "Unlimited";
        if (c.TryGetProperty("has_credits", out var has) && has.ValueKind == JsonValueKind.False) return null;
        return c.TryGetProperty("balance", out var b) ? b.ToString() : null;
    }
}

// Local fallback: the Codex CLI records the rate_limits it receives in its session logs.
// Only lines carrying "rate_limits" are parsed; prompts and other content are skipped without being kept.
public sealed class CodexSessionLogCollector : IUsageCollector
{
    public string Name => "SessionLog";
    public string Kind => "codex-session-log";
    public string Level => "Deep";
    public int MinIntervalSeconds => 10;
    public bool TokenFreeVerified => true;
    public string Verification => Loc.Msg("vf.sessionLog");
    public bool Implemented => true;

    public async Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var sessions = Path.Combine(CodexProvider.CodexHome(context.Account), "sessions");
        if (!Directory.Exists(sessions)) return CollectorResult.Fail(Loc.Msg("msg.noSessions"));

        var files = await Task.Run(() => new DirectoryInfo(sessions)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .Where(f => f.LastWriteTime > DateTime.Now.AddDays(-8))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(5)
            .ToList(), cancellationToken);

        foreach (var file in files)
        {
            var line = await LastRateLimitLine(file, cancellationToken);
            if (line == null) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object) continue;
            var eventAt = root.TryGetProperty("timestamp", out var ts) && DateTimeOffset.TryParse(ts.GetString(), out var parsedTs) ? parsedTs : new DateTimeOffset(file.LastWriteTime);
            var primary = Window(limits, "primary", eventAt);
            var secondary = Window(limits, "secondary", eventAt);
            var age = DateTimeOffset.Now - eventAt;
            string? plan = limits.TryGetProperty("plan_type", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

            return new CollectorResult
            {
                Success = true,
                Confidence = age < TimeSpan.FromMinutes(15) ? "Medium" : "Low",
                Message = Loc.Msg("msg.sessionLog", eventAt.ToLocalTime().ToString("MM-dd HH:mm")),
                Detail = Loc.Msg("msg.lastEvent", eventAt.ToLocalTime().ToString("MM-dd HH:mm:ss")),
                SessionPercent = primary.Percent,
                SessionResetAt = primary.ResetAt,
                WeeklyPercent = secondary.Percent,
                WeeklyResetAt = secondary.ResetAt,
                Plan = plan,
                CreditsBalance = CodexUsageEndpointCollector.Credits(limits)
            };
        }

        return CollectorResult.Fail(Loc.Msg("msg.noRateLimitEvent"));
    }

    // The raw line found per file, valid while the file's length and write time are unchanged. Only the line is kept:
    // the values derived from it depend on the current time and are computed again on every collection.
    private static readonly object LineCacheLock = new();
    private static readonly Dictionary<string, (long Length, long WrittenTicks, string? Line)> LineCache = new();
    private const int FirstTailBytes = 256 * 1024;

    private static async Task<string?> LastRateLimitLine(FileInfo file, CancellationToken cancellationToken)
    {
        var length = file.Length;
        var written = file.LastWriteTimeUtc.Ticks;
        lock (LineCacheLock)
        {
            if (LineCache.TryGetValue(file.FullName, out var cached) && cached.Length == length && cached.WrittenTicks == written) return cached.Line;
        }

        var line = await ReadLastRateLimitLine(file.FullName, cancellationToken);
        lock (LineCacheLock)
        {
            if (LineCache.Count > 32) LineCache.Clear();
            LineCache[file.FullName] = (length, written, line);
        }
        return line;
    }

    // Session logs grow for as long as a session lasts and the newest rate_limits event is near the end, so read
    // from the end: a window that grows (x4) until a matching line is found, ending at the whole file at worst.
    private static async Task<string?> ReadLastRateLimitLine(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        var length = stream.Length;
        var window = Math.Min(length, FirstTailBytes);
        while (true)
        {
            var start = length - window;
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[window];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            // A window that starts mid-file begins inside a line: skip to the first line break (0x0A never occurs
            // inside a multi-byte UTF-8 character). No break at all means the window is inside one long line.
            var from = 0;
            if (start > 0)
            {
                var lineBreak = Array.IndexOf(buffer, (byte)'\n');
                from = lineBreak < 0 ? buffer.Length : lineBreak + 1;
            }
            var found = LastMatchingLine(System.Text.Encoding.UTF8.GetString(buffer, from, buffer.Length - from));
            if (found != null) return found;
            if (start == 0) return null;
            window = Math.Min(length, window * 4);
        }
    }

    private static string? LastMatchingLine(string text)
    {
        var end = text.Length;
        while (end > 0)
        {
            var lineBreak = text.LastIndexOf('\n', end - 1);
            var line = text.AsSpan(lineBreak + 1, end - lineBreak - 1).TrimEnd('\r');
            if (line.IndexOf("\"rate_limits\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"used_percent\"", StringComparison.Ordinal) >= 0) return line.ToString();
            end = lineBreak < 0 ? 0 : lineBreak;
        }
        return null;
    }

    private static (double? Percent, DateTimeOffset? ResetAt) Window(JsonElement limits, string name, DateTimeOffset eventAt)
    {
        if (!limits.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
        double? percent = w.TryGetProperty("used_percent", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number) reset = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()).ToLocalTime();
        else if (w.TryGetProperty("resets_in_seconds", out var rs) && rs.ValueKind == JsonValueKind.Number) reset = eventAt.AddSeconds(rs.GetDouble());
        // The logged value predates a reset that has since happened.
        if (reset.HasValue && reset.Value <= DateTimeOffset.Now) percent = 0;
        return (percent, reset);
    }
}
