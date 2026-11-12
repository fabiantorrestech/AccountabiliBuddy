using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// What a goal aims for (brief §5.1): an amount per period, and optionally a long-term amount by a date.
/// </summary>
/// <param name="Period">The span <paramref name="Amount"/> is for.</param>
/// <param name="Amount">
/// The amount per period; null = no period target (a long-term target only, or Deep work's "Track time only"). With
/// <paramref name="WeeklyDays"/> set it is the amount for each of those days instead (a habit's is 1).
/// </param>
/// <param name="TargetAmount">The long-term amount (a total since the goal began, or the level to reach); null = none.</param>
/// <param name="TargetDate">The date <paramref name="TargetAmount"/> is for; null = no date.</param>
/// <param name="WeeklyDays">"3 of 7 days": how many days of each period must be hit; null = the period's amount counts instead.</param>
/// <param name="MissesAllowed">For a daily goal: missed days a week before it reads Behind; null = none (an addition to §5.1, Q361).</param>
/// <param name="PendingChange">A looser amount waiting for its delay (Deep work only today, §5.7); null = none.</param>
public sealed record GoalTarget(
    GoalPeriod Period,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Amount,
    GoalUnit Unit,
    GoalDirection Direction = GoalDirection.AtLeast,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? TargetAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? TargetDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WeeklyDays = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MissesAllowed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GoalPendingChange? PendingChange = null)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }

    /// <summary>Whether a day can be hit on its own: a daily target, or "N of the period's days".</summary>
    [JsonIgnore]
    public bool CountsDays => Period == GoalPeriod.Day || WeeklyDays is not null;
}
