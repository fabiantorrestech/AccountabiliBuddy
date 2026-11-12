namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A goal's day, computed (brief §5.1), never stored: the heatmap cell, the day panel, the 14-day strip.
/// </summary>
/// <param name="Value">
/// The day's value in the goal's unit: minutes for a Time goal, the day's value for an Amount goal (0 with
/// <paramref name="HasValue"/> false when nothing was logged), 1 or 0 for a habit, the hand total for a milestone.
/// </param>
/// <param name="Minutes">The time its time sources counted that day (focus, calendar, hand time), whole minutes; for "6 of 15 min".</param>
/// <param name="HasValue">Whether anything counted or was logged that day.</param>
/// <param name="Hit">Whether the day met its own target (only for goals that count days: <see cref="GoalTarget.CountsDays"/>).</param>
/// <param name="Short">How far from the day's target: "N short" (or over an at-most cap) for a past day, "N to go" today; 0 otherwise.</param>
/// <param name="Sources">The kinds of source that gave it something, in rule order (and Hand last when a hand entry counted).</param>
/// <param name="ByHand">Whether a hand entry decided or added to it.</param>
/// <param name="BackFilled">Whether a hand entry for it was made after the day.</param>
public sealed record GoalDayState(
    DateOnly Day,
    double Value,
    double Minutes,
    bool HasValue,
    bool Hit,
    GoalDayStatus Status,
    double Short,
    IReadOnlyList<GoalSourceType> Sources,
    bool ByHand,
    bool BackFilled);
