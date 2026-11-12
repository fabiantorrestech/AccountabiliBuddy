using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The Labels screen's edits of the labels themselves (ADR 76, spec 03 §C, 05 §16), each stamped so the newest version wins on every
/// device (<see cref="SharedListMerge"/>): a new label (the name-based id, a colour of the 12-colour set or a custom one), a new colour
/// (any form <see cref="TaskLabelColors"/> takes, opacity kept) and its "Rings on" devices (stamped on their own, see
/// <see cref="TaskLabelMerge"/>). Renaming and deleting go through the PC's
/// category list (<see cref="TaskCategoryEdits"/>), which also carries them to a running checklist. Ids only change for a new label.
/// </summary>
public static class TaskLabelEdits
{
    /// <summary>Whether <paramref name="name"/> can be the label's (<paramref name="labelId"/>: the label being renamed; null for a new one).</summary>
    public static TaskLabelNameCheck CheckName(IEnumerable<TaskLabel> labels, Guid? labelId, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return TaskLabelNameCheck.Blank;
        }

        bool taken = labels.Any(label => label.IsLive && label.Id != labelId && TaskLabel.SameName(label.Name, trimmed));
        return taken ? TaskLabelNameCheck.Taken : TaskLabelNameCheck.Fine;
    }

    /// <summary>
    /// A new label at the end of the list, in <paramref name="color"/> (when it isn't a colour: the first of the set no label has). A name
    /// that is blank or already a live label's adds nothing (<paramref name="id"/> null).
    /// </summary>
    public static TaskLists Add(TaskLists lists, string name, string? color, DateTimeOffset now, out Guid? id)
    {
        List<TaskLabel> labels = lists.Labels.Select(item => item.Label).ToList();
        id = null;
        if (CheckName(labels, null, name) != TaskLabelNameCheck.Fine)
        {
            return lists;
        }

        string trimmed = name.Trim();
        Guid newId = TaskLabelBook.FreeIdFor(trimmed, labels);
        string chosen = TaskLabelColors.IsValid(color) ? color!.Trim() : TaskLabelColors.FirstUnused(labels);
        double position = labels.Where(label => label.IsLive).Select(label => label.Position).DefaultIfEmpty(0).Max() + 1;
        int existing = labels.FindIndex(label => label.Id == newId);
        // A tombstone of the same id comes back: stamped after it, so the revive wins everywhere.
        DateTimeOffset stamp = existing >= 0 ? After(now, labels[existing].DeletedAt ?? labels[existing].EditedAt) : now;
        var label = new TaskLabel(newId, trimmed, chosen, position, stamp);
        if (existing >= 0)
        {
            labels[existing] = label;
        }
        else
        {
            labels.Add(label);
        }

        id = newId;
        return With(lists, labels);
    }

    /// <summary>The label in <paramref name="color"/> (its opacity kept); unchanged for a colour that can't be read, the same colour or no such live label.</summary>
    public static TaskLists Recolour(TaskLists lists, Guid labelId, string color, DateTimeOffset now)
    {
        if (!TaskLabelColors.IsValid(color))
        {
            return lists;
        }

        string wanted = color.Trim();
        return Changed(lists, labelId, label => string.Equals(label.Color, wanted, StringComparison.OrdinalIgnoreCase)
            ? null
            : label with { Color = wanted, EditedAt = After(now, label.EditedAt) });
    }

    /// <summary>
    /// The phones the label's reminders ring on (null: every device), stamped on its own (<see cref="TaskLabel.RingOnStamp"/>: now, or
    /// just after a stamp ahead) by <paramref name="self"/>; unchanged when it already rings on those.
    /// </summary>
    public static TaskLists SetRingOn(TaskLists lists, Guid labelId, IReadOnlyList<Guid>? ringOn, DateTimeOffset now, Guid self)
    {
        IReadOnlyList<Guid>? wanted = TaskRingDevices.Normalized(ringOn);
        return Changed(lists, labelId, label => TaskRingDevices.Same(label.RingOn, wanted)
            ? null
            : label with
            {
                RingOn = wanted,
                RingOnStamp = new TaskFieldStamp(label.RingOnStamp is { } held ? After(now, held.At) : now, self),
                EditedAt = After(now, label.EditedAt),
            });
    }

    // The lists with one live label changed (change returns null: nothing to change).
    private static TaskLists Changed(TaskLists lists, Guid labelId, Func<TaskLabel, TaskLabel?> change)
    {
        List<TaskLabel> labels = lists.Labels.Select(item => item.Label).ToList();
        int index = labels.FindIndex(label => label.Id == labelId && label.IsLive);
        if (index < 0 || change(labels[index]) is not TaskLabel changed)
        {
            return lists;
        }

        labels[index] = changed;
        return With(lists, labels);
    }

    private static TaskLists With(TaskLists lists, List<TaskLabel> labels)
    {
        return lists with { Labels = labels.Select(label => new SyncTaskLabel(label)).ToList() };
    }

    private static DateTimeOffset After(DateTimeOffset now, DateTimeOffset held)
    {
        return held >= now ? held.AddTicks(1) : now;
    }
}
