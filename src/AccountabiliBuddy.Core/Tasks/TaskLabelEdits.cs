using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The Labels screen's edits of the labels themselves (ADR 76, spec 03 §C, 05 §16), each stamped so the newest version wins on every
/// device (<see cref="SharedListMerge"/>): a new label (the name-based id, a colour of the 12-colour set or a custom one), a new colour
/// (any form <see cref="TaskLabelColors"/> takes, opacity kept) and its "Rings on" devices (stamped on their own, see
/// <see cref="TaskLabelMerge"/>) and its "Only on" devices (ADR 89, the same way: a stamp of its own). Renaming and deleting go through the PC's
/// category list (<see cref="TaskCategoryEdits"/>), which also carries them to a running checklist. Ids only change for a new label.
/// </summary>
public static class TaskLabelEdits
{
    /// <summary>
    /// Whether <paramref name="name"/> can be the label's (<paramref name="labelId"/>: the label being renamed; null for a new one). A label
    /// this device can't see (device-only on other devices, ADR 89) doesn't make the name taken: <see cref="Add"/> joins it. With no
    /// <paramref name="self"/> (empty) every label is seen.
    /// </summary>
    public static TaskLabelNameCheck CheckName(IEnumerable<TaskLabel> labels, Guid? labelId, string name, Guid self = default, IReadOnlyCollection<Guid>? removed = null)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return TaskLabelNameCheck.Blank;
        }

        bool taken = labels.Any(label => label.IsLive && label.Id != labelId && TaskLabel.SameName(label.Name, trimmed) && TaskScopes.LabelVisible(label, self, removed));
        return taken ? TaskLabelNameCheck.Taken : TaskLabelNameCheck.Fine;
    }

    /// <summary>
    /// A new label at the end of the list, in <paramref name="color"/> (when it isn't a colour: the first of the set no label has). A name
    /// that is blank or already a live label's adds nothing (<paramref name="id"/> null). A deleted label that comes back this way rings on
    /// every device again, stamped by <paramref name="self"/> at the revive.
    /// </summary>
    /// <param name="onlyOnThisDevice">
    /// ADR 89: the label is device-only on <paramref name="self"/>. A label of that name that this device can't see (device-only elsewhere) is
    /// joined instead: this device is added to its list (a stamped edit, <paramref name="id"/> is that label's), so the same name made on two
    /// devices is one label shared by them. Asked shared, the hidden label becomes shared (the user chose that name for everyone). A device
    /// with no id can't be named: it makes a shared label.
    /// </param>
    /// <param name="removed">The devices removed from the group (ADR 54): their device-only labels count as shared.</param>
    public static TaskLists Add(
        TaskLists lists, string name, string? color, DateTimeOffset now, Guid self, out Guid? id, bool onlyOnThisDevice = false, IReadOnlyCollection<Guid>? removed = null)
    {
        List<TaskLabel> labels = lists.Labels.Select(item => item.Label).ToList();
        id = null;
        if (CheckName(labels, null, name, self, removed) != TaskLabelNameCheck.Fine)
        {
            return lists;
        }

        string trimmed = name.Trim();
        bool deviceOnly = onlyOnThisDevice && self != Guid.Empty;
        int hidden = labels.FindIndex(label => label.IsLive && TaskLabel.SameName(label.Name, trimmed));
        if (hidden >= 0)
        {
            // A label this device can't see: joined, or made shared.
            id = labels[hidden].Id;
            return SetOnlyOn(lists, labels[hidden].Id, deviceOnly ? Joined(labels[hidden].OnlyOn, self) : null, now, self);
        }

        Guid newId = TaskLabelBook.FreeIdFor(trimmed, labels);
        string chosen = TaskLabelColors.IsValid(color) ? color!.Trim() : TaskLabelColors.FirstUnused(labels);
        double position = labels.Where(label => label.IsLive).Select(label => label.Position).DefaultIfEmpty(0).Max() + 1;
        int existing = labels.FindIndex(label => label.Id == newId);
        // A tombstone of the same id comes back: stamped after it, so the revive wins everywhere.
        DateTimeOffset stamp = existing >= 0 ? After(now, labels[existing].DeletedAt ?? labels[existing].EditedAt) : now;
        var label = new TaskLabel(newId, trimmed, chosen, position, stamp);
        if (existing >= 0)
        {
            // Revived: rings on every device and shown on every device again unless asked otherwise, both stamped at the revive.
            labels[existing] = label with { RingOnStamp = new TaskFieldStamp(stamp, self), OnlyOn = deviceOnly ? [self] : null, OnlyOnStamp = new TaskFieldStamp(stamp, self) };
        }
        else
        {
            labels.Add(deviceOnly ? label with { OnlyOn = [self], OnlyOnStamp = new TaskFieldStamp(stamp, self) } : label);
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

    /// <summary>
    /// The devices that show the label (null: every device), stamped on its own (<see cref="TaskLabel.OnlyOnStamp"/>: now, or just after
    /// a stamp ahead) by <paramref name="self"/>; unchanged when it already is shown on those. "Make shared" is null; "only on this
    /// device" is a list of <paramref name="self"/>, or <see cref="Joined"/> when other devices hold it too (ADR 89). A choice is display only,
    /// so it applies at once, in a session too.
    /// </summary>
    public static TaskLists SetOnlyOn(TaskLists lists, Guid labelId, IReadOnlyList<Guid>? onlyOn, DateTimeOffset now, Guid self)
    {
        IReadOnlyList<Guid>? wanted = TaskScopes.Normalized(onlyOn);
        return Changed(lists, labelId, label => TaskRingDevices.Same(TaskScopes.Normalized(label.OnlyOn), wanted)
            ? null
            : label with
            {
                OnlyOn = wanted,
                OnlyOnStamp = new TaskFieldStamp(label.OnlyOnStamp is { } held ? After(now, held.At) : now, self),
                EditedAt = After(now, label.EditedAt),
            });
    }

    /// <summary>A device list with <paramref name="self"/> added (a list of just <paramref name="self"/> for a shared label).</summary>
    public static IReadOnlyList<Guid> Joined(IReadOnlyList<Guid>? onlyOn, Guid self)
    {
        return TaskScopes.Normalized((onlyOn ?? []).Append(self).ToList())!;
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
