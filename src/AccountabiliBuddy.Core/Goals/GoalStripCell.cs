namespace AccountabiliBuddy.Core.Goals;

/// <summary>One day of the 14-day strip (<see cref="GoalStrip14"/>).</summary>
/// <param name="Progress">Today only: how much of the day's target is done, 0 to 1 (8 of 15 = about half); 0 for other days.</param>
/// <param name="IsToday">Today's cell has the accent outline, hit or not.</param>
public sealed record GoalStripCell(DateOnly Day, GoalStripCellKind Kind, double Progress, bool IsToday);
