namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// When and where one part of a goal (<see cref="GoalField"/>) was last changed, for the field-by-field goal merge
/// (<see cref="GoalSyncMerge"/>). The goals' twin of a task's stamp (ADR 76) without bases: goals never show "Changed on both devices".
/// </summary>
/// <param name="At">When it was changed.</param>
/// <param name="By">The device that changed it; the empty id for a value nobody changed on a device (made by the first run, or a goal stored before goals synced).</param>
public sealed record GoalFieldStamp(DateTimeOffset At, Guid By);
