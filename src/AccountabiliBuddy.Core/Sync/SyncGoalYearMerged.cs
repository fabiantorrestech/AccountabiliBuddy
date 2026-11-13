namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One goal year file a device has merged (ADR 79 shared history, GS1b), as its sync file's <c>goalYearsMerged</c> lists it: JSON
/// <c>{"year":2026,"rolledAt":"2027-02-01T09:00:00+00:00"}</c>. The roll-up time names the year file's version (a later roll-up of the same
/// year only adds and gets a later time).
/// </summary>
/// <param name="Year">The calendar year (UTC) the year file holds.</param>
/// <param name="RolledAt">The merged version's <see cref="Goals.GoalHistoryYear.RolledAt"/>.</param>
public sealed record SyncGoalYearMerged(int Year, DateTimeOffset RolledAt);
