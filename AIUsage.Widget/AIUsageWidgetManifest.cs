using Dora.Widget.Abstractions;

namespace AIUsage.Widget;

public static class AIUsageWidgetManifest
{
    // Immutable after release; never derive it from the display name.
    public const string WidgetId = "dev.leedoha.aiusage.summary";

    // Events this widget publishes / commands it handles (<domain>.<resource>.<action>).
    public const string UsageUpdatedEvent = "aiusage.usage.updated";
    public const string ProviderChangedEvent = "aiusage.provider.changed";
    public const string RefreshCommand = "aiusage.usage.refresh";
    public const string SelectAccountCommand = "aiusage.account.select";

    // State schema of what SaveStateAsync writes; see FeatureStateSnapshot.
    public static int StateVersion => Core.Storage.FeatureStateSnapshot.CurrentVersion;

    public static WidgetManifest Create() => new()
    {
        Id = WidgetId,
        Name = "AI Usage",
        Version = typeof(AIUsageWidgetManifest).Assembly.GetName().Version ?? new Version(0, 9, 0),
        ContractVersion = ContractInfo.Current,
        Layout = new WidgetLayoutProfile
        {
            NaturalSize = new WidgetSize(330, 200),
            CompactSize = new WidgetSize(230, 130),
            CollapsedSize = new WidgetSize(150, 36),
            MinNaturalSize = new WidgetSize(300, 110),
            MinCompactSize = new WidgetSize(190, 64),
            MinCollapsedSize = new WidgetSize(120, 30)
        },
        // Credentials/session logs (FileSystem), usage endpoints (Network), CLI login window (ProcessExecution),
        // threshold and reset alerts (Notifications).
        Capabilities = WidgetCapabilities.FileSystem | WidgetCapabilities.Network |
                       WidgetCapabilities.ProcessExecution | WidgetCapabilities.Notifications,
        // Several accounts live inside one widget, so a second instance would only duplicate polling.
        AllowMultipleInstances = false,
        SupportsDetailView = true,
        SupportsFloating = true,
        Description = "Claude / Codex / Copilot usage limits and reset timers.",
        Author = "leedoha",
        IconKey = "aiusage",
        Category = "Monitoring"
    };
}
