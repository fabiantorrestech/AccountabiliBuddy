using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// This PC's goals in a backup (ADR 71; round 2 R4 review), next to the task lists and in the same kinds: the lists in their stored
/// shape, deletions included, readable inside the backup (sealed only by the backup's own password, never by this PC's key, so they
/// restore on another PC). Names travel like task titles. Notes, reminder reasons and review notes are in it only when the backup has a
/// password (<paramref name="IncludesNotes"/>); without one they are left out and a restore keeps this PC's. The phone ignores it.
/// </summary>
public sealed record GoalsExport(
    IReadOnlyList<GoalArea>? Areas,
    IReadOnlyList<Goal>? Goals,
    IReadOnlyList<GoalEntry>? Entries,
    IReadOnlyList<GoalReminder>? Reminders,
    IReadOnlyList<GoalReviewRecord>? Reviews,
    bool IncludesNotes);
