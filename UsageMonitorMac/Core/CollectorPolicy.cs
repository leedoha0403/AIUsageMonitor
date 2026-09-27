namespace UsageMonitorMac.Core;

public static class CollectorPolicy
{
    public static int LevelRank(string level) => level switch
    {
        "Safe" => 0,
        "Standard" => 1,
        "Deep" => 2,
        _ => 1
    };
}
