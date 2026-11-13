using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// An active device of the group as the history rules see it (<see cref="GoalHistoryRules.Roller"/>, <see cref="GoalHistoryRules.MayRemoveMonth"/>):
/// its id, whether it is a PC, and its sync file's "merged year files" marker (<see cref="SyncFile.GoalYearsMerged"/>; null when it sent none).
/// </summary>
public sealed record GoalHistoryDevice(Guid Id, bool IsPc, IReadOnlyList<SyncGoalYearMerged>? YearsMerged);
