using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>A day's status for a goal (brief §5.1, change note §4a). Computed, never stored.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalDayStatus>))]
public enum GoalDayStatus
{
    /// <summary>Hit with the user's own hand in it (a tick, a typed value, time added by hand).</summary>
    Counted,

    /// <summary>"Counted · ticked itself": hit by its sources alone (focus, a reading).</summary>
    CountedTickedItself,

    /// <summary>"Not counted · N short": some progress, under the day's target (or over an at-most cap); see <see cref="GoalDayState.Short"/>.</summary>
    NotCounted,

    /// <summary>A past day that asked for something and got nothing.</summary>
    Missed,

    /// <summary>Today, not hit yet ("In progress · N min to go").</summary>
    InProgress,

    /// <summary>Inside a pause stretch: neither hit nor missed.</summary>
    Paused,

    /// <summary>Nothing was asked of the day: before the goal existed, in the future, or a day a weekly goal didn't need.</summary>
    NotDue,
}
