using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AIUsage.Core;

namespace AIUsage.Core.Providers;

// GitHub Copilot has no 5H/weekly windows, only a monthly premium-request quota.
// It is stored in the "long window" fields (WeeklyUsagePercent/WeeklyResetAt) and labeled Monthly.
public sealed class CopilotProvider : IUsageProvider
{
    public string ProviderId => "copilot";
    public string DisplayName => "GitHub Copilot";
    public string SignInHint => Loc.Msg("hint.copilot");
    public ProviderCapabilities Capabilities { get; } = new() { SessionUsage = false, LongWindow = "Monthly", MultiAccount = true, ModelBreakdown = true };
    public IReadOnlyList<IUsageCollector> Collectors { get; } =
    [
        new CopilotUserEndpointCollector(),
        new LocalCacheCollector()
    ];
}

// Reuses the Copilot CLI login (read-only) and calls the endpoint the Copilot IDE extensions use for their
// quota display. It is a status lookup, not a model call, so it does not consume premium requests.
public sealed class CopilotUserEndpointCollector : IUsageCollector
{
    public string Name => "Official";
    public string Kind => "copilot-user-endpoint";
    public string Level => "Standard";
    public int MinIntervalSeconds => 60;
    public bool TokenFreeVerified => true;
    public string Verification => Loc.Msg("vf.copilotOfficial");
    public bool Implemented => true;

    public async Task<CollectorResult> CollectAsync(CollectContext context, CancellationToken cancellationToken)
    {
        var configDir = CopilotLogin.ConfigDirectory(context.Account);
        var user = CopilotLogin.Resolve(context.Account);
        var tokens = CopilotLogin.Tokens(context.Account, user);
        if (tokens.Count == 0)
        {
            return CollectorResult.SignedOut(Loc.Msg("msg.copilotNoLogin"),
                user == null ? Loc.Msg("msg.searched", Path.Combine(configDir, "config.json")) : Loc.Msg("msg.copilotNoToken", user.Login));
        }

        CollectorResult? lastRejected = null;
        foreach (var (token, label) in tokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CopilotLogin.ApiBase(user?.Host) + "/copilot_internal/user");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", token);
            request.Headers.Add("X-GitHub-Api-Version", "2025-04-01");
            using var response = await Http.Client.SendAsync(request, cancellationToken);
            var detail = Loc.Msg("msg.credential", $"{label} · HTTP {(int)response.StatusCode}");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // An env token may belong to another app; fall through to the CLI's stored login.
                lastRejected = CollectorResult.SignedOut(Loc.Msg("msg.copilotRejected"), detail);
                continue;
            }
            if (!response.IsSuccessStatusCode) return CollectorResult.Fail(Loc.Msg("msg.httpError", (int)response.StatusCode), detail);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return Parse(body.RootElement, detail);
        }
        return lastRejected!;
    }

    private static CollectorResult Parse(JsonElement root, string detail)
    {
        var reset = Date(root, "quota_reset_date_utc") ?? Date(root, "quota_reset_date") ?? Date(root, "limited_user_reset_date");
        var models = new List<ModelUsage>();
        double? primary = null;

        if (root.TryGetProperty("quota_snapshots", out var snapshots) && snapshots.ValueKind == JsonValueKind.Object)
        {
            foreach (var (key, label) in new[] { ("premium_interactions", "Premium"), ("chat", "Chat"), ("completions", "Completions") })
            {
                if (!snapshots.TryGetProperty(key, out var q) || q.ValueKind != JsonValueKind.Object) continue;
                if (q.TryGetProperty("unlimited", out var u) && u.ValueKind == JsonValueKind.True) continue;
                var entitlement = Number(q, "entitlement");
                var remaining = Number(q, "remaining");
                double? used = Number(q, "percent_remaining") is { } left ? 100 - left
                    : entitlement is > 0 && remaining.HasValue ? (entitlement.Value - remaining.Value) / entitlement.Value * 100 : null;
                if (!used.HasValue) continue;
                var name = entitlement is > 0 && remaining.HasValue ? $"{label} {entitlement.Value - remaining.Value:0}/{entitlement.Value:0}" : label;
                models.Add(new ModelUsage { Name = name, Percent = (int)Math.Round(Math.Clamp(used.Value, 0, 100)), ResetAt = reset });
                primary ??= used;
            }
        }
        // Copilot Free reports remaining counts (limited_user_quotas) against monthly totals (monthly_quotas).
        else if (root.TryGetProperty("limited_user_quotas", out var left) && root.TryGetProperty("monthly_quotas", out var total))
        {
            foreach (var (key, label) in new[] { ("chat", "Chat"), ("completions", "Completions") })
            {
                if (Number(total, key) is not > 0 || Number(left, key) is not { } remaining) continue;
                var entitlement = Number(total, key)!.Value;
                var used = (entitlement - remaining) / entitlement * 100;
                models.Add(new ModelUsage { Name = $"{label} {entitlement - remaining:0}/{entitlement:0}", Percent = (int)Math.Round(Math.Clamp(used, 0, 100)), ResetAt = reset });
                primary ??= used;
            }
        }

        // "" (not null) clears an overage left over from the previous month.
        var overage = "";
        if (root.TryGetProperty("quota_snapshots", out var s2) && s2.TryGetProperty("premium_interactions", out var p) && p.ValueKind == JsonValueKind.Object &&
            Number(p, "overage_count") is { } count && count > 0)
        {
            overage = Loc.Msg("msg.copilotOverage", count.ToString("0"));
        }

        return new CollectorResult
        {
            Success = true,
            Confidence = "High",
            Message = Loc.Msg("msg.official"),
            Detail = detail,
            // Unlimited plans have no quota to exhaust: report 0% used.
            WeeklyPercent = primary ?? 0,
            WeeklyResetAt = reset,
            Plan = PlanName(root.TryGetProperty("copilot_plan", out var plan) && plan.ValueKind == JsonValueKind.String ? plan.GetString() : null),
            ModelBreakdown = models,
            CreditsBalance = overage
        };
    }

    private static string? PlanName(string? plan) => plan switch
    {
        null or "" => null,
        "free" => "Free",
        "individual" => "Pro",
        "individual_pro" => "Pro+",
        "business" => "Business",
        "enterprise" => "Enterprise",
        _ => plan
    };

    private static DateTimeOffset? Date(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

// Where the Copilot CLI keeps its login: config.json names the signed-in user, the token itself is in the
// Windows Credential Manager (or in an env var, which the CLI also honors). Everything here is read-only.
public static class CopilotLogin
{
    public sealed record User(string Host, string Login);

    private const string CredentialService = "copilot-cli";

    public static string ConfigDirectory(UsageProviderState account)
    {
        if (!string.IsNullOrWhiteSpace(account.ConfigDirectory)) return Environment.ExpandEnvironmentVariables(account.ConfigDirectory);
        var env = Environment.GetEnvironmentVariable("COPILOT_HOME");
        return !string.IsNullOrWhiteSpace(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
    }

    // The account's chosen login, or the CLI's current one when none is chosen.
    public static User? Resolve(UsageProviderState account)
    {
        var (current, all) = ReadUsers(ConfigDirectory(account));
        if (string.IsNullOrWhiteSpace(account.Login)) return current;
        return all.FirstOrDefault(u => u.Login.Equals(account.Login, StringComparison.OrdinalIgnoreCase))
               ?? new User(current?.Host ?? "https://github.com", account.Login.Trim());
    }

    // Every login signed in to the Copilot CLI in this account's folder (for the account settings picker).
    public static IReadOnlyList<User> SignedInUsers(UsageProviderState account) => ReadUsers(ConfigDirectory(account)).All;

    private static (User? Current, List<User> All) ReadUsers(string configDirectory)
    {
        var all = new List<User>();
        try
        {
            var path = Path.Combine(configDirectory, "config.json");
            if (!File.Exists(path)) return (null, all);
            // The CLI writes "//" comment lines above the JSON.
            var json = string.Join('\n', File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith("//")));
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement.TryGetProperty("lastLoggedInUser", out var u) ? ToUser(u) : null;
            if (doc.RootElement.TryGetProperty("loggedInUsers", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                all.AddRange(list.EnumerateArray().Select(ToUser).OfType<User>());
            }
            if (current != null && !all.Any(x => x.Login.Equals(current.Login, StringComparison.OrdinalIgnoreCase))) all.Insert(0, current);
            return (current, all);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, all);
        }
    }

    private static User? ToUser(JsonElement u)
    {
        if (u.ValueKind != JsonValueKind.Object) return null;
        var login = u.TryGetProperty("login", out var l2) && l2.ValueKind == JsonValueKind.String ? l2.GetString() : null;
        var host = u.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
        return string.IsNullOrEmpty(login) ? null : new User(string.IsNullOrEmpty(host) ? "https://github.com" : host, login);
    }

    // Candidate tokens in the CLI's own precedence: env vars (default folder and login only), then the stored login.
    public static List<(string Token, string Label)> Tokens(UsageProviderState account, User? user)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(account.ConfigDirectory) && string.IsNullOrWhiteSpace(account.Login))
        {
            foreach (var name in new[] { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" })
            {
                if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) list.Add((value, name));
            }
        }
        if (user != null && StoredToken(user) is { } stored) list.Add((stored, $"Credential Manager · {user.Login}"));
        return list;
    }

    public static string ApiBase(string? host)
    {
        if (string.IsNullOrEmpty(host) || !Uri.TryCreate(host, UriKind.Absolute, out var uri) || uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.github.com";
        }
        // GHE.com data residency uses api.<tenant>.ghe.com; GitHub Enterprise Server uses <host>/api/v3.
        return uri.Host.EndsWith(".ghe.com", StringComparison.OrdinalIgnoreCase) ? $"https://api.{uri.Host}" : $"{uri.Scheme}://{uri.Host}/api/v3";
    }

    // The CLI stores one generic credential per login, named "<service>/<account>" where the account part
    // contains the host and login. Match on the login so a second signed-in account is never picked by mistake.
    private static string? StoredToken(User user)
    {
        if (!CredEnumerate(CredentialService + "/*", 0, out var count, out var list)) return null;
        var host = Uri.TryCreate(user.Host, UriKind.Absolute, out var hostUri) ? hostUri.Host : "github.com";
        try
        {
            string? hostMatch = null;
            for (var i = 0; i < count; i++)
            {
                var cred = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                var target = cred.TargetName ?? "";
                var accountPart = target.Length > CredentialService.Length + 1 ? target[(CredentialService.Length + 1)..] : "";
                if (!EndsWithLogin(accountPart, user.Login) && !EndsWithLogin(cred.UserName ?? "", user.Login)) continue;
                if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero) continue;
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                var token = Encoding.UTF8.GetString(bytes).Trim('\0', ' ', '\r', '\n');
                if (token.Length == 0) continue;
                if (accountPart.Contains(host, StringComparison.OrdinalIgnoreCase)) return token;
                hostMatch ??= token;
            }
            return hostMatch;
        }
        finally
        {
            CredFree(list);
        }
    }

    private static bool EndsWithLogin(string value, string login) =>
        value.Equals(login, StringComparison.OrdinalIgnoreCase) ||
        (value.EndsWith(login, StringComparison.OrdinalIgnoreCase) && value.Length > login.Length && value[^(login.Length + 1)] is ':' or '/' or '@');

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
