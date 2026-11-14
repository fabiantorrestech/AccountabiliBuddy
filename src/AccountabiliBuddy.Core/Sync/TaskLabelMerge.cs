using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two label lists (ADR 76, 89): item by item, newest version first (<see cref="SharedListMerge"/>), then each live label's
/// "Rings on" and "Only on" on their own: the copy with the newest stamp (<see cref="TaskLabel.RingOnStamp"/>, <see cref="TaskLabel.OnlyOnStamp"/>)
/// gives the field (a stamped list beats an unstamped one; between two stamps the later time, then the device id, then the list; a stamp far
/// ahead counts as <see cref="SharedListMerge.FutureLimit"/> ahead). So a newer change to the label's name or colour made without the
/// newest rings on or device list (a PC rebuilding the label from an older phone's category) never takes the choice back, and every device
/// picks the same whatever order the lists come in. A stamp with a null "Only on" is a choice of "shared" and wins like any other. Label
/// tombstones are kept for good (ids and stamps only, no text): an older phone keeps its categories for good, and a tombstone is what stops
/// one bringing a deleted label back.
/// </summary>
public static class TaskLabelMerge
{
    // One stamped field of a label: how its stamp, its text and its value are read and set.
    private sealed record Field(
        Func<TaskLabel, TaskFieldStamp?> Stamp,
        Func<TaskLabel, string> Text,
        Func<TaskLabel, TaskLabel, TaskLabel> Take,
        Func<TaskLabel, TaskFieldStamp, TaskLabel> Clamp);

    private static readonly Field RingOn = new(
        label => label.RingOnStamp,
        label => TaskRingDevices.Text(label.RingOn),
        (label, source) => label with { RingOn = source.RingOn, RingOnStamp = source.RingOnStamp },
        (label, stamp) => label with { RingOnStamp = stamp });

    private static readonly Field OnlyOn = new(
        label => label.OnlyOnStamp,
        label => TaskRingDevices.Text(TaskScopes.Normalized(label.OnlyOn)),
        (label, source) => label with { OnlyOn = source.OnlyOn, OnlyOnStamp = source.OnlyOnStamp },
        (label, stamp) => label with { OnlyOnStamp = stamp });

    public static List<SyncTaskLabel> Merge(IEnumerable<SyncTaskLabel> held, IEnumerable<SyncTaskLabel> incoming, DateTimeOffset now)
    {
        List<SyncTaskLabel> heldList = held.ToList();
        List<SyncTaskLabel> incomingList = incoming.ToList();
        List<SyncTaskLabel> merged = SharedListMerge.Merge(heldList, incomingList, now, keepTombstones: true);
        DateTimeOffset limit = now + SharedListMerge.FutureLimit;
        List<TaskLabel> all = heldList.Concat(incomingList).Select(item => item.Label).Where(label => label.IsLive).ToList();

        merged = WithNewest(merged, all, limit, RingOn);
        return WithNewest(merged, all, limit, OnlyOn).Select(WithStoredOnlyOn).ToList();
    }

    // A live label's device list in its stored form (no repeat, id order, none is null), whichever way it arrived.
    private static SyncTaskLabel WithStoredOnlyOn(SyncTaskLabel item)
    {
        if (!item.Label.IsLive || item.Label.OnlyOn is null)
        {
            return item;
        }

        IReadOnlyList<Guid>? stored = TaskScopes.Normalized(item.Label.OnlyOn);
        return stored is not null && stored.SequenceEqual(item.Label.OnlyOn) ? item : new SyncTaskLabel(item.Label with { OnlyOn = stored });
    }

    // Each live item gets the field of the live copy with the newest stamp; items with no stamped copy stay as the whole-record merge left them.
    private static List<SyncTaskLabel> WithNewest(List<SyncTaskLabel> merged, List<TaskLabel> all, DateTimeOffset limit, Field field)
    {
        var newest = new Dictionary<Guid, TaskLabel>();
        foreach (TaskLabel label in all.Where(label => field.Stamp(label) is not null))
        {
            TaskLabel clamped = Clamped(label, limit, field);
            if (!newest.TryGetValue(label.Id, out TaskLabel? best) || Wins(clamped, best, field))
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

            bool same = field.Text(item.Label) == field.Text(source) && field.Stamp(item.Label) == field.Stamp(source);
            return same ? item : new SyncTaskLabel(field.Take(item.Label, source));
        }).ToList();
    }

    // Both stamped: the later time, then the device id, then the list, so the choice never depends on the order.
    private static bool Wins(TaskLabel candidate, TaskLabel held, Field field)
    {
        TaskFieldStamp first = field.Stamp(candidate)!;
        TaskFieldStamp second = field.Stamp(held)!;
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

        return string.CompareOrdinal(field.Text(candidate), field.Text(held)) > 0;
    }

    private static TaskLabel Clamped(TaskLabel label, DateTimeOffset limit, Field field)
    {
        return field.Stamp(label) is { } stamp && stamp.At > limit ? field.Clamp(label, new TaskFieldStamp(limit, stamp.By)) : label;
    }
}
