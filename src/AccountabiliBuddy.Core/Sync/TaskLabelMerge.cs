using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two label lists (ADR 76): item by item, newest version first (<see cref="SharedListMerge"/>), then each live label's "Rings on"
/// on its own: the copy with the newest <see cref="TaskLabel.RingOnStamp"/> gives it (a stamped list beats an unstamped one; between two
/// stamps the later time, then the device id, then the list; a stamp far ahead counts as <see cref="SharedListMerge.FutureLimit"/> ahead).
/// So a newer change to the label's name or colour made without the newest rings on (a PC rebuilding the label from an older phone's
/// category) never takes the choice back, and every device picks the same whatever order the lists come in. Label tombstones are kept
/// for good (ids and stamps only, no text): an older phone keeps its categories for good, and a tombstone is what stops one bringing a
/// deleted label back.
/// </summary>
public static class TaskLabelMerge
{
    public static List<SyncTaskLabel> Merge(IEnumerable<SyncTaskLabel> held, IEnumerable<SyncTaskLabel> incoming, DateTimeOffset now)
    {
        List<SyncTaskLabel> heldList = held.ToList();
        List<SyncTaskLabel> incomingList = incoming.ToList();
        List<SyncTaskLabel> merged = SharedListMerge.Merge(heldList, incomingList, now, keepTombstones: true);
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;

        var newest = new Dictionary<Guid, TaskLabel>();
        foreach (TaskLabel label in heldList.Concat(incomingList).Select(item => item.Label).Where(label => label.IsLive && label.RingOnStamp is not null))
        {
            TaskLabel clamped = Clamped(label, limit);
            if (!newest.TryGetValue(label.Id, out TaskLabel? best) || Wins(clamped, best))
            {
                newest[label.Id] = clamped;
            }
        }

        return merged.Select(item =>
        {
            if (!item.Label.IsLive || !newest.TryGetValue(item.Id, out TaskLabel? source))
            {
                return item;
            }

            bool same = TaskRingDevices.Same(item.Label.RingOn, source.RingOn) && item.Label.RingOnStamp == source.RingOnStamp;
            return same ? item : new SyncTaskLabel(item.Label with { RingOn = source.RingOn, RingOnStamp = source.RingOnStamp });
        }).ToList();
    }

    // Both stamped: the later time, then the device id, then the list, so the choice never depends on the order.
    private static bool Wins(TaskLabel candidate, TaskLabel held)
    {
        TaskFieldStamp first = candidate.RingOnStamp!;
        TaskFieldStamp second = held.RingOnStamp!;
        int byTime = first.At.UtcTicks.CompareTo(second.At.UtcTicks);
        if (byTime != 0)
        {
            return byTime > 0;
        }

        int byDevice = string.CompareOrdinal(first.By.ToString("D"), second.By.ToString("D"));
        if (byDevice != 0)
        {
            return byDevice > 0;
        }

        return string.CompareOrdinal(TaskRingDevices.Text(candidate.RingOn), TaskRingDevices.Text(held.RingOn)) > 0;
    }

    private static TaskLabel Clamped(TaskLabel label, DateTimeOffset limit)
    {
        return label.RingOnStamp is { } stamp && stamp.At > limit ? label with { RingOnStamp = new TaskFieldStamp(limit, stamp.By) } : label;
    }
}
