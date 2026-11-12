using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The built-in Deep work goal's own values on this PC (document "goals.deepWork", device-local: plan R4.4, the "goal" document travels
/// in sync files, so adding a field there would be a new synced field). Its daily target is not here: that stays the "goal" document
/// (<see cref="GoalService"/>, ADR 29). Holds no private text, so it isn't sealed. A device setting in backups, restored through the
/// Deep work rules (<see cref="GoalsService.RestoreDeepWorkAsync"/>): a lower weekly target or "Track time only" in a backup waits.
/// </summary>
/// <param name="WeeklyMinutes">The weekly target in minutes; null = none (the default).</param>
/// <param name="TrackTimeOnly">
/// "Track time only, no target": Goals shows Deep work without a daily or weekly target. The guards keep the "goal" document's daily
/// minutes as they are (they never read this; Q374).
/// </param>
/// <param name="PendingDaily">A lower daily target waiting for its delay (applied through <see cref="GoalService.SetAsync"/>).</param>
/// <param name="PendingWeekly">A lower weekly target (or none) waiting for its delay.</param>
/// <param name="PendingTrackTimeOnly">"Track time only" waiting for its delay (its amount is null).</param>
public sealed record DeepWorkGoalSettings(
    int? WeeklyMinutes = null,
    bool TrackTimeOnly = false,
    GoalPendingChange? PendingDaily = null,
    GoalPendingChange? PendingWeekly = null,
    GoalPendingChange? PendingTrackTimeOnly = null)
{
    public static DeepWorkGoalSettings Default { get; } = new();

    /// <summary>Whether any change is waiting.</summary>
    public bool HasPending => PendingDaily is not null || PendingWeekly is not null || PendingTrackTimeOnly is not null;
}
