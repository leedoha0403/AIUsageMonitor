namespace UsageMonitorWpf.Core;

public static class CollectorPolicy
{
    public static readonly string[] Levels = ["Safe", "Standard", "Deep"];

    public static int LevelRank(string level) => Math.Max(0, Array.IndexOf(Levels, level));

    // Only token-free collectors allowed by the current level run automatically.
    public static bool IsAllowed(CollectorInfo collector, string collectionLevel, bool implemented) =>
        implemented && collector.TokenFreeVerified && LevelRank(collector.Level) <= LevelRank(collectionLevel);

    public static string DisabledReason(CollectorInfo collector, string collectionLevel, bool implemented)
    {
        if (!implemented || !collector.TokenFreeVerified) return Loc.Msg("msg.disabledUnverified");
        return Loc.Msg("msg.disabledLevel", collectionLevel, collector.Level);
    }
}
