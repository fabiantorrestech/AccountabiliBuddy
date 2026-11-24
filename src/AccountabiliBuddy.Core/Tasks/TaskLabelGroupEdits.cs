using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The edits of the label groups themselves (ADR 93; "categories" in the UI): add, rename, icon, order and delete, each stamped so the newest
/// version wins on every device (<see cref="TaskLabelGroupMerge"/>). A group is always shared (no device list). Deleting one leaves a tombstone
/// kept for good and never edits its labels: they show uncategorised (<see cref="TaskLabelGroupBook.GroupOf"/>). Moving a label into or out of a
/// group is a label edit (<see cref="TaskLabelEdits.SetGroup"/>). Ids only change for a new group.
/// </summary>
public static class TaskLabelGroupEdits
{
    /// <summary>
    /// Whether <paramref name="name"/> can be the group's (<paramref name="groupId"/>: the group being renamed; null for a new one): not blank,
    /// and no other live group has it (case-insensitive, trimmed).
    /// </summary>
    public static TaskLabelNameCheck CheckName(IEnumerable<SyncTaskLabelGroup>? groups, Guid? groupId, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return TaskLabelNameCheck.Blank;
        }

        bool taken = TaskLabelGroupBook.Live(groups).Any(group => group.Id != groupId && TaskLabel.SameName(group.Name, trimmed));
        return taken ? TaskLabelNameCheck.Taken : TaskLabelNameCheck.Fine;
    }

    /// <summary>
    /// A new group at the end of the list, with <paramref name="icon"/> (an unknown key: the default). A name that is blank or already a live
    /// group's adds nothing (<paramref name="id"/> null). A deleted group of that name-id comes back, stamped after its tombstone so the revive
    /// wins everywhere.
    /// </summary>
    public static TaskLists Add(TaskLists lists, string name, string? icon, DateTimeOffset now, out Guid? id)
    {
        id = null;
        if (CheckName(lists.LabelGroups, null, name) != TaskLabelNameCheck.Fine)
        {
            return lists;
        }

        string trimmed = name.Trim();
        List<TaskLabelGroup> groups = All(lists);
        Guid newId = TaskLabelGroupBook.FreeIdFor(trimmed, groups);
        double position = groups.Where(group => group.IsLive).Select(group => group.Position).DefaultIfEmpty(0).Max() + 1;
        int existing = groups.FindIndex(group => group.Id == newId);
        DateTimeOffset stamp = existing >= 0 ? After(now, groups[existing].DeletedAt ?? groups[existing].EditedAt) : now;
        var group = new TaskLabelGroup(newId, trimmed, TaskLabelGroupIcons.Chosen(icon), position, stamp);
        if (existing >= 0)
        {
            groups[existing] = group;
        }
        else
        {
            groups.Add(group);
        }

        id = newId;
        return With(lists, groups);
    }

    /// <summary>The group renamed (its id stays); unchanged for a blank or taken name, the same name or no such live group.</summary>
    public static TaskLists Rename(TaskLists lists, Guid groupId, string name, DateTimeOffset now)
    {
        if (CheckName(lists.LabelGroups, groupId, name) != TaskLabelNameCheck.Fine)
        {
            return lists;
        }

        string trimmed = name.Trim();
        return Changed(lists, groupId, group => string.Equals(group.Name, trimmed, StringComparison.Ordinal)
            ? null
            : group with { Name = trimmed, EditedAt = After(now, group.EditedAt) });
    }

    /// <summary>The group's icon (an unknown key: the default); unchanged for the same icon or no such live group.</summary>
    public static TaskLists SetIcon(TaskLists lists, Guid groupId, string icon, DateTimeOffset now)
    {
        string chosen = TaskLabelGroupIcons.Chosen(icon);
        return Changed(lists, groupId, group => string.Equals(group.Icon, chosen, StringComparison.Ordinal)
            ? null
            : group with { Icon = chosen, EditedAt = After(now, group.EditedAt) });
    }

    /// <summary>
    /// The live groups in <paramref name="order"/> (ids of live groups; others are ignored), each at 1, 2, 3...; only a group whose position
    /// changes is stamped. Live groups not named keep their place after the named ones.
    /// </summary>
    public static TaskLists Reorder(TaskLists lists, IReadOnlyList<Guid> order, DateTimeOffset now)
    {
        List<TaskLabelGroup> groups = All(lists);
        List<Guid> live = TaskLabelGroupBook.Live(lists.LabelGroups).Select(group => group.Id).ToList();
        List<Guid> wanted = order.Where(live.Contains).Distinct().Concat(live.Where(id => !order.Contains(id))).ToList();
        bool changed = false;
        for (int place = 0; place < wanted.Count; place++)
        {
            int index = groups.FindIndex(group => group.Id == wanted[place] && group.IsLive);
            double position = place + 1;
            if (groups[index].Position != position)
            {
                groups[index] = groups[index] with { Position = position, EditedAt = After(now, groups[index].EditedAt) };
                changed = true;
            }
        }

        return changed ? With(lists, groups) : lists;
    }

    /// <summary>The group as a tombstone (kept for good); its labels are left as they are and show uncategorised. Unchanged for no such live group.</summary>
    public static TaskLists Delete(TaskLists lists, Guid groupId, DateTimeOffset now)
    {
        return Changed(lists, groupId, group => group.Deleted(After(now, group.EditedAt)));
    }

    // Every group record, tombstones included.
    private static List<TaskLabelGroup> All(TaskLists lists)
    {
        return (lists.LabelGroups ?? []).Select(item => item.Group).ToList();
    }

    // The lists with one live group changed (change returns null: nothing to change).
    private static TaskLists Changed(TaskLists lists, Guid groupId, Func<TaskLabelGroup, TaskLabelGroup?> change)
    {
        List<TaskLabelGroup> groups = All(lists);
        int index = groups.FindIndex(group => group.Id == groupId && group.IsLive);
        if (index < 0 || change(groups[index]) is not TaskLabelGroup changed)
        {
            return lists;
        }

        groups[index] = changed;
        return With(lists, groups);
    }

    private static TaskLists With(TaskLists lists, List<TaskLabelGroup> groups)
    {
        return lists with { LabelGroups = groups.Select(group => new SyncTaskLabelGroup(group)).ToList() };
    }

    private static DateTimeOffset After(DateTimeOffset now, DateTimeOffset held)
    {
        return held >= now ? held.AddTicks(1) : now;
    }
}
