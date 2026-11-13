namespace AccountabiliBuddy.Core.Goals;

/// <summary>What <see cref="GoalSyncMerge.MergeReported"/> gives: the merged lists and the entries dropped because their goal isn't held.</summary>
/// <param name="Lists">The merged lists (<see cref="GoalSyncMerge.Merge"/>).</param>
/// <param name="DroppedEntries">Entries (tombstones too) whose goal neither input holds, live or as a tombstone; ordered by id. Empty normally.</param>
public sealed record GoalSyncMergeResult(GoalSyncLists Lists, IReadOnlyList<GoalEntry> DroppedEntries);
