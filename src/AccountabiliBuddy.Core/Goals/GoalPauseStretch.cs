using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A stretch a goal was paused (brief §5.4): neither hit nor missed, skipped by streaks, left out of "This week" and the goal's
/// age. Pausing applies at once, so the day it was paused is paused; turning it back on resumes from that day.
/// </summary>
/// <param name="From">The first paused day.</param>
/// <param name="ResumedOn">The day it was turned back on (that day counts again); null while it is still paused.</param>
public sealed record GoalPauseStretch(
    DateOnly From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? ResumedOn = null)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    /// <summary>Whether <paramref name="day"/> is inside the stretch.</summary>
    public bool Covers(DateOnly day)
    {
        return day >= From && (ResumedOn is not DateOnly resumed || day < resumed);
    }
}
