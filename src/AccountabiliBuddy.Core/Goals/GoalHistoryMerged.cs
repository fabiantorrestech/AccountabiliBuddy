namespace AccountabiliBuddy.Core.Goals;

/// <summary>What <see cref="GoalHistoryRules.Merge"/> gives (ADR 79 shared history, GS1b).</summary>
/// <param name="Lists">The device's lists after the goals part and the history rows.</param>
/// <param name="Read">
/// The history files to record as read at their listed version (the transport's <c>lastRead</c>, <see cref="GoalHistoryRules.ToRead"/>): every
/// file given except one that brought a row the merge dropped because its goal isn't held yet. Such a file stays unread, so it is read
/// again on a later sync, once a goals part has brought the goal.
/// </param>
/// <param name="Dropped">The rows dropped because their goal isn't held (live or tombstone), ordered by id.</param>
/// <param name="GoalsPartMerged">Whether a regular file's goals part took part. A device stays "new to the group" (reads every file) until
/// one has (<see cref="GoalHistoryRules.StillNewToGroup"/>).</param>
public sealed record GoalHistoryMerged(GoalSyncLists Lists, IReadOnlyList<GoalHistoryListing> Read, IReadOnlyList<GoalEntry> Dropped, bool GoalsPartMerged);
