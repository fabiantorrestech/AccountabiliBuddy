using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Merges two label-group lists (ADR 93) item by item with <see cref="SharedListMerge"/>: the newest version of each group wins, a time far
/// ahead is clamped to 2 minutes ahead, and tombstones are kept for good (ids, position and stamps only), like label tombstones: a device
/// that was away longer than 30 days must not bring a deleted group back. Either side null means none; nothing left is null, so a device
/// that never made a group keeps writing none. Entries the merge can't use (no group, no id, a live one with no name or a blank one) are left out.
/// </summary>
public static class TaskLabelGroupMerge
{
    public static List<SyncTaskLabelGroup>? Merge(IEnumerable<SyncTaskLabelGroup>? held, IEnumerable<SyncTaskLabelGroup>? incoming, DateTimeOffset now)
    {
        List<SyncTaskLabelGroup> merged = SharedListMerge.Merge(Usable(held), Usable(incoming), now, keepTombstones: true);
        return merged.Count == 0 ? null : merged;
    }

    /// <summary>
    /// A received or stored part as the merge takes it: entries with no group or no id, and live ones with no name, are left out (leaving one
    /// out never deletes it: the merge keeps what it has); a missing icon becomes the default and a tombstone's missing name an empty one. Null
    /// stays null (a file without the part).
    /// </summary>
    public static List<SyncTaskLabelGroup>? Clean(IEnumerable<SyncTaskLabelGroup>? groups)
    {
        return groups?.Where(IsUsable)
            .Select(item => item.Group.Icon is null || item.Group.Name is null
                ? item with { Group = item.Group with { Icon = item.Group.Icon ?? TaskLabelGroupIcons.Default, Name = item.Group.Name ?? string.Empty } }
                : item)
            .ToList();
    }

    private static IEnumerable<SyncTaskLabelGroup> Usable(IEnumerable<SyncTaskLabelGroup>? groups)
    {
        return Clean(groups) ?? [];
    }

    private static bool IsUsable(SyncTaskLabelGroup? item)
    {
        // Read from JSON, the non-nullable fields can still be missing.
        TaskLabelGroup? group = item?.Group;
        return group is not null && group.Id != Guid.Empty && (group.DeletedAt is not null || (group.Name is not null && group.Name.Trim().Length > 0));
    }
}
