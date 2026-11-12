using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Which way a target points (brief §5.1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalDirection>))]
public enum GoalDirection
{
    /// <summary>At least the amount (focus minutes, steps).</summary>
    AtLeast,

    /// <summary>At most the amount (screen time).</summary>
    AtMost,

    /// <summary>
    /// Reach a level (a weight): up or down, whichever way the target lies from the goal's first value
    /// (<see cref="GoalCounting.ReachesDown"/>).
    /// </summary>
    Reach,
}
