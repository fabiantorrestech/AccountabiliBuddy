using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>The two kinds of goal history file (ADR 79 shared history, GS1b); JSON by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalHistoryFileKind>))]
public enum GoalHistoryFileKind
{
    /// <summary>One device's month (<see cref="GoalHistoryMonth"/>).</summary>
    Month,

    /// <summary>A finished year's roll-up (<see cref="GoalHistoryYear"/>).</summary>
    Year,
}
