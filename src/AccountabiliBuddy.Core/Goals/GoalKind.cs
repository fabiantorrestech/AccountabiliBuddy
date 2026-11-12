using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What sort of goal it is (round 2 brief §5.1). Written by name; the phone's twin uses the same names.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalKind>))]
public enum GoalKind
{
    /// <summary>Yes or no each day: ticked by hand, or ticking itself once a linked time threshold is met.</summary>
    Habit,

    /// <summary>A value over time (weight, steps, a count): a day's value is a hand edit, else the day's first reading.</summary>
    Amount,

    /// <summary>Minutes: focus time on labels or #tags, calendar time, time added by hand. Deep work is one.</summary>
    Time,

    /// <summary>A milestone with its own tasks (the task list is "Coming soon" until ADR 79); done by hand for now.</summary>
    Milestone,
}
