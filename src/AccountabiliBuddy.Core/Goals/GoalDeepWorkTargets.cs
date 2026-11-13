namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// Deep work's weekly target and "Track time only" as they travel in its goal record (ADR 79; <see cref="GoalDeepWorkSync"/>): on the
/// way out this device's EFFECTIVE values (never one waiting for its delay), on the way in the values another device sent, which the
/// receiver applies through its own looser-change rule (GS3). The daily target never travels here (ADR 29: the "goal" document).
/// </summary>
/// <param name="WeeklyMinutes">The weekly target in minutes; null = none.</param>
/// <param name="TrackTimeOnly">"Track time only, no target".</param>
/// <param name="Stamp">When and where these were last changed (the record's <see cref="GoalField.SecondTarget"/> stamp).</param>
public sealed record GoalDeepWorkTargets(double? WeeklyMinutes, bool TrackTimeOnly, GoalFieldStamp Stamp);
