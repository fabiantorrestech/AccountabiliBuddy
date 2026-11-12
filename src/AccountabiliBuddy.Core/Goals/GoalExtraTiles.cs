using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Edit goal › "Extra tiles" (change note §4a): Streak and Total live in Stats and show on the goal only when switched on.</summary>
public sealed record GoalExtraTiles(bool Streak = false, bool Total = false)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    public static GoalExtraTiles None { get; } = new();
}
