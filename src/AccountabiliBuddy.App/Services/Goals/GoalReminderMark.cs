namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// One goal reminder occurrence's state on this PC (round 2 R5.5): ids and times only, no goal text. Device-local, never synced
/// (ADR 79 is Proposed).
/// </summary>
/// <param name="At">When it was marked (old marks are pruned).</param>
/// <param name="SnoozedUntil">For <see cref="GoalReminderMarkKind.Snoozed"/>: when it rings again.</param>
/// <param name="SnoozeCount">How many times this occurrence was snoozed.</param>
public sealed record GoalReminderMark(GoalReminderMarkKind Kind, DateTimeOffset At, DateTimeOffset? SnoozedUntil = null, int SnoozeCount = 0)
{
    /// <summary>It doesn't ring again (dismissed, skipped or rung out).</summary>
    public bool IsHandled => Kind != GoalReminderMarkKind.Snoozed;
}
