using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>How a 14-day strip cell is drawn (change note §4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalStripCellKind>))]
public enum GoalStripCellKind
{
    /// <summary>Solid.</summary>
    Hit,

    /// <summary>Hollow with a faint fill: some time, under the target.</summary>
    Partial,

    /// <summary>Hollow: a thin outline, no fill.</summary>
    Missed,

    /// <summary>Hatched: neither hit nor missed.</summary>
    Paused,

    /// <summary>Nothing asked (before the goal existed, a day a weekly goal didn't need): blank.</summary>
    NotDue,

    /// <summary>Today, not hit yet: accent outline, filled from the bottom by <see cref="GoalStripCell.Progress"/>.</summary>
    Today,
}
