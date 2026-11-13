using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A device of the sync group as this device knows it, before the history rules pick the active ones (<see cref="GoalHistoryRules.ActiveDevices"/>).
/// </summary>
/// <param name="Id">The device id.</param>
/// <param name="IsPc">Whether it is a PC (only a PC rolls year files, <see cref="GoalHistoryRules.Roller"/>).</param>
/// <param name="Removed">Whether it was removed from the group (it is never active again; its past values still count).</param>
/// <param name="LastRegularMerge">When this device last merged that device's regular sync file (its trusted now); null for never. Not a full
/// history read: only a regular file shows the device is still syncing.</param>
/// <param name="YearsMerged">Its regular sync file's "merged year files" marker (<see cref="SyncFile.GoalYearsMerged"/>); null when it sent none.</param>
public sealed record GoalGroupDevice(Guid Id, bool IsPc, bool Removed, DateTimeOffset? LastRegularMerge, IReadOnlyList<SyncGoalYearMerged>? YearsMerged);
