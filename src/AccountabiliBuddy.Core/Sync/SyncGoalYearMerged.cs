namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One goal year file a device has merged (ADR 79 shared history, GS1b), as its sync file's <c>goalYearsMerged</c> lists it: JSON
/// <c>{"year":2026,"rolledBy":"11111111-0000-0000-0000-000000000001","rolledAt":"2027-02-01T09:00:00+00:00"}</c>. The year and the rolling PC
/// name the file (<see cref="Goals.GoalHistoryFileKey.ForYear"/>); the roll-up time names its version (a later roll-up of the same file only
/// adds and gets a later time). An item without <c>rolledBy</c> (read as the empty id) matches no year file.
/// </summary>
/// <param name="Year">The calendar year (UTC) the year file holds.</param>
/// <param name="RolledBy">The PC that rolled the merged year file (<see cref="Goals.GoalHistoryYear.RolledBy"/>).</param>
/// <param name="RolledAt">The merged version's <see cref="Goals.GoalHistoryYear.RolledAt"/>.</param>
public sealed record SyncGoalYearMerged(int Year, Guid RolledBy, DateTimeOffset RolledAt);
