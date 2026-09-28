using UsageMonitorWpf.Providers;

namespace UsageMonitorWpf.Core;

public static class Defaults
{
    public static readonly string[] ProviderOrder = ["claude", "codex", "copilot"];

    public static AppState CreateState()
    {
        var state = new AppState();
        foreach (var id in ProviderOrder)
        {
            state.Providers[id] = CreateAccount(id, id, "Personal");
        }
        return state;
    }

    public static UsageProviderState CreateAccount(string providerId, string accountKey, string accountName, string configDirectory = "")
    {
        var provider = ProviderRegistry.Get(providerId);
        var now = DateTimeOffset.Now;
        var account = new UsageProviderState
        {
            AccountKey = accountKey,
            ProviderId = providerId,
            DisplayName = provider.DisplayName,
            AccountName = accountName,
            ConfigDirectory = configDirectory,
            Plan = "Unknown",
            SessionResetAt = now.AddHours(5),
            WeeklyResetAt = now.AddDays(7),
            CollectedAt = now,
            FieldSources = ManualSources()
        };
        // Copilot was added after Claude/Codex: don't surface a signed-out card to people who never used it.
        if (providerId == "copilot" && accountKey == providerId) account.Enabled = LoginHelper.FindCli(providerId) != null;
        SyncDefinition(account);
        return account;
    }

    // Brings a saved account in line with the current provider definition (new collectors, capabilities),
    // keeping the runtime status of collectors that already existed.
    public static void SyncDefinition(UsageProviderState account)
    {
        var provider = ProviderRegistry.Get(account.ProviderId);
        account.DisplayName = provider.DisplayName;
        account.Capabilities = provider.Capabilities;
        var previous = account.Collectors.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        account.Collectors = provider.Collectors.Select(definition =>
        {
            var info = ProviderRegistry.Describe(definition);
            if (previous.TryGetValue(info.Name, out var old))
            {
                info.Status = old.Status;
                info.LatencyMs = old.LatencyMs;
                info.LastRunAt = old.LastRunAt;
                info.LastSuccessAt = old.LastSuccessAt;
                info.Message = old.Message;
                info.Detail = old.Detail;
            }
            return info;
        }).ToList();
    }

    private static Dictionary<string, FieldSource> ManualSources()
    {
        return new Dictionary<string, FieldSource>
        {
            ["sessionUsagePercent"] = new() { Source = "Manual", Confidence = "Low" },
            ["sessionResetAt"] = new() { Source = "Manual", Confidence = "Low" },
            ["weeklyUsagePercent"] = new() { Source = "Manual", Confidence = "Low" },
            ["weeklyResetAt"] = new() { Source = "Manual", Confidence = "Low" },
            ["plan"] = new() { Source = "Manual", Confidence = "Low" }
        };
    }
}
