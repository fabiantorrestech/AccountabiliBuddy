using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// ADR 79's part of the sync file on this PC (GS3a): the five goal lists (<c>goals</c>, <c>goalAreas</c>, <c>goalEntries</c>,
/// <c>goalReminders</c>, <c>goalReviews</c>) go into this PC's own file and come in from the other devices' files, only while this PC
/// shares its task lists (ADR 71; the caller, <see cref="SyncCoordinator"/>, checks the switch). Out: the store's lists with Deep work's
/// record composed with this PC's EFFECTIVE weekly target and its own stamp (<see cref="GoalDeepWorkSync.Compose"/>), entries edited in
/// the last 90 days only (<see cref="GoalSyncWindow"/>), never a waiting change (<see cref="SyncGoalParts.Into"/>); none at all while the
/// goals aren't ready (a store not migrated or unreadable sends no part, never an empty list). In: merged item by item and goal part by
/// part (<see cref="GoalSyncMerge"/>, never the backup rule <see cref="GoalStore.ImportAsync"/>) and taken in through
/// <see cref="GoalsService.ApplySyncedAsync"/> (Deep work's received weekly target through this PC's looser-change rule); refused while the
/// goals aren't ready. A file without goal parts (an older app, a QR file, a device that doesn't share its task lists) leaves the goals as
/// they are. Logs nothing: goal names, notes and reasons are private text. Use from the UI thread only.
/// </summary>
public sealed class GoalListSync
{
    private readonly GoalsService _goals;
    private readonly Func<Guid?> _self;
    private readonly TimeProvider _time;

    /// <summary>ADR 79 shared history (GS3b): the year files this PC has merged, sent as <c>goalYearsMerged</c>; set by the app.</summary>
    public Func<IReadOnlyList<SyncGoalYearMerged>?>? YearsMerged { get; set; }

    /// <summary>ADR 79 shared history (GS3b): a regular file's goal parts merged here (the device is syncing; its year-file marker); set by the app.</summary>
    public Action<SyncFile>? Merged { get; set; }

    /// <param name="self">This PC's device id in its group; null while it isn't in one.</param>
    /// <param name="time">The trusted clock (ADR 70): the merge's "now" and the receipt time of a received lower target.</param>
    public GoalListSync(GoalsService goals, Func<Guid?> self, TimeProvider time)
    {
        _goals = goals;
        _self = self;
        _time = time;
    }

    /// <summary>This PC's file with its goal parts; the file as it is (no goal part) while the goals aren't ready.</summary>
    public SyncFile Decorate(SyncFile file)
    {
        if (HeldLists() is not GoalSyncLists lists)
        {
            return file with { Goals = null, GoalAreas = null, GoalEntries = null, GoalReminders = null, GoalReviews = null };
        }

        return SyncGoalParts.Into(file, GoalSyncWindow.Apply(lists, _time.GetUtcNow())) with { GoalYearsMerged = YearsMerged?.Invoke() };
    }

    /// <summary>Takes another device's goal parts in; true when this PC's goals changed.</summary>
    public async Task<bool> ReceiveAsync(SyncFile file)
    {
        if (_self() is Guid me && file.Device.DeviceId == me)
        {
            return false;
        }

        if (!_goals.Store.IsReady || SyncGoalParts.From(file) is not GoalSyncLists incoming)
        {
            return false;
        }

        // The held lists are read and merged inside the service's gate, not before it (a change saved while this waits is not lost).
        bool changed = await _goals.ApplySyncedAsync(
            () => HeldLists() is GoalSyncLists held ? GoalSyncMerge.MergeReported(held, incoming, _time.GetUtcNow()).Lists : null, this);
        Merged?.Invoke(file);
        return changed;
    }

    /// <summary>
    /// This PC's lists as the sync file carries them (Deep work composed; none of the 90-day window applied): what a history file's rows
    /// merge into. Null while the goals aren't ready.
    /// </summary>
    public GoalSyncLists? Held() => HeldLists();

    // Everything held here (null while the goals aren't ready), Deep work's record composed: the merge sees this PC's effective weekly
    // target with its own stamp.
    private GoalSyncLists? HeldLists()
    {
        if (!_goals.Store.IsReady || _goals.DeepWorkTargetsForSync is not GoalDeepWorkTargets targets)
        {
            return null;
        }

        GoalSyncLists lists = GoalStore.ToSync(_goals.Lists);
        IReadOnlyList<Goal> known = lists.Goals is GoalNewerList<Goal> carrier ? carrier.Known : lists.Goals;
        List<Goal> composed = known.Select(goal => goal.Id == Goal.DeepWorkId && goal.IsLive ? GoalDeepWorkSync.Compose(goal, targets) : goal).ToList();
        return lists with { Goals = GoalNewerJson.WithNewer(composed, GoalNewerJson.NewerOf(lists.Goals)) };
    }
}
