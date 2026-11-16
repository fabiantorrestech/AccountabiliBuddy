using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Which steps a goal's steps source counts (ADR 91). The absence of a value (null on <see cref="GoalSourceRule.StepsFrom"/>) means all,
/// the whole-phone total Health Connect de-duplicates, which is what a goal counted before this existed. Written by name in the casing
/// the goal enums use; an unknown name from a newer app makes the rule a kept newer item (<see cref="GoalEnumConverter{T}"/>). Twin:
/// Kotlin <c>GoalStepsFrom</c>.
/// </summary>
[JsonConverter(typeof(GoalEnumConverter<GoalStepsFrom>))]
public enum GoalStepsFrom
{
    /// <summary>Steps recorded by a watch, band or ring; on a day with none, the phone's steps (and the day's entry says which it used).</summary>
    Watch,

    /// <summary>Steps recorded by the phone itself.</summary>
    Phone,
}
