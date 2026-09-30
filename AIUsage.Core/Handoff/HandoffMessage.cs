using System.Text.Json.Serialization;

namespace AIUsage.Core.Handoff;

// One JSON line on the pipe. A single flat shape keeps the protocol easy to version: unknown fields are ignored,
// and fields a message does not use stay at their defaults.
public sealed class HandoffMessage
{
    public string Type { get; set; } = "";

    // Correlation: a reply carries the Id of the request it answers.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ReplyTo { get; set; }

    public int Protocol { get; set; }
    public int Pid { get; set; }
    public string? AppVersion { get; set; }

    // Feature state being handed over (FeatureStateSnapshot: version + JSON).
    public int StateVersion { get; set; }
    public string? StateJson { get; set; }

    // Virtual-screen physical pixels + dpi of the monitor the position belongs to.
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Dpi { get; set; } = 96;

    // Cursor position for hover/drop messages (physical pixels).
    public double CursorX { get; set; }
    public double CursorY { get; set; }

    public bool Accepted { get; set; }
    public string? Reason { get; set; }

    [JsonIgnore]
    public bool IsReply => ReplyTo != null;
}

public static class HandoffTypes
{
    public const int ProtocolVersion = 1;

    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Adopt = "adopt";
    public const string Adopted = "adopted";
    public const string DockHover = "dockHover";
    public const string DockHoverEnd = "dockHoverEnd";
    public const string DockRequest = "dockRequest";
    public const string Docked = "docked";
    public const string Revoke = "revoke";
    public const string Bye = "bye";
}
