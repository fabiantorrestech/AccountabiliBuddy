using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Backup;

/// <summary>
/// This PC's goals in a backup (ADR 71; round 2 R4 review), next to the task lists and in the same kinds: the lists in their stored
/// shape, deletions included, readable inside the backup (sealed only by the backup's own password, never by this PC's key, so they
/// restore on another PC). Names travel like task titles. Notes, reminder reasons and review notes are in it only when the backup has a
/// password (<paramref name="IncludesNotes"/>); without one they are left out and a restore keeps this PC's. The phone ignores it.
/// </summary>
/// <remarks>
/// ADR 79 forward compatibility: a backup's item that holds a value this version doesn't know is skipped on restore (the rest is taken
/// in) rather than refusing the backup's goals; unknown fields of the items taken in come with them.
/// </remarks>
public sealed record GoalsExport(
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalArea>))] IReadOnlyList<GoalArea>? Areas,
    [property: JsonConverter(typeof(GoalNewerListConverter<Goal>))] IReadOnlyList<Goal>? Goals,
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalEntry>))] IReadOnlyList<GoalEntry>? Entries,
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalReminder>))] IReadOnlyList<GoalReminder>? Reminders,
    [property: JsonConverter(typeof(GoalNewerListConverter<GoalReviewRecord>))] IReadOnlyList<GoalReviewRecord>? Reviews,
    bool IncludesNotes);
