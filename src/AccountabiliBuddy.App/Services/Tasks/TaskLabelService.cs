using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The Labels screen's path to the labels (ADR 76, spec 03 §C, 05 §16): every edit is a stamped change of the task list, so it syncs.
/// A new label, a colour and "Rings on" are label edits (<see cref="TaskLabelEdits"/>); a rename and a delete go through the PC's
/// category list (<see cref="SessionTasksService"/>, as the old editor did), which also carries them to a running session's checklist.
/// No lock applies: naming or colouring a group of tasks loosens nothing, so it works during a focus session too (as before). Use from
/// the UI thread only.
/// </summary>
public sealed class TaskLabelService
{
    private readonly TaskStore _store;
    private readonly SessionTasksService _tasks;

    public TaskLabelService(TaskStore store, SessionTasksService tasks)
    {
        _store = store;
        _tasks = tasks;
        store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The task list changed (a label, or the tasks the counts read).</summary>
    public event EventHandler? Changed;

    /// <summary>This device is in a sync group, so "Only on this device" can be offered.</summary>
    public bool CanScope => _store.CanScope;

    /// <summary>Whether the label counts as shared (ADR 89).</summary>
    public bool IsShared(TaskLabel label) => _store.IsShared(label);

    /// <summary>"on Laptop, Phone" for a device-only label several (not all) devices share; null otherwise.</summary>
    public string? DevicesLine(TaskLabel label) => _store.DevicesLine(label.OnlyOn);

    /// <summary>Whether a tag counts as shared (ADR 89).</summary>
    public bool IsTagShared(string tag) => _store.IsTagShared(tag);

    /// <summary>"on Laptop, Phone" for a device-only tag several (not all) devices share; null otherwise.</summary>
    public string? TagDevicesLine(string tag) => _store.DevicesLine(TaskScopes.ScopeOf(tag, _store.Lists.TagScopes)?.OnlyOn);

    /// <summary>The live labels this device shows (ADR 89: other devices' device-only ones stay hidden), in the list's order.</summary>
    public IReadOnlyList<TaskLabel> Labels
    {
        get
        {
            // The test is built once for the whole list (it reads the removed devices), not once per label.
            Func<TaskLabel, bool> shown = _store.LabelShown;
            return _store.Lists.Labels
                .Select(item => item.Label)
                .Where(label => label.IsLive && label.Name.Trim().Length > 0 && shown(label))
                .OrderBy(label => label.Position).ThenBy(label => label.Id.ToString("D"), StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>ADR 93: the live label groups ("categories"), in their order; none while the task list isn't active.</summary>
    public IReadOnlyList<TaskLabelGroup> Groups => _store.IsActive ? TaskLabelGroupBook.Live(_store.Lists.LabelGroups) : [];

    /// <summary>How many tasks have the label: the task list's live tasks, plus a running session's checklist tasks (held out of the list).</summary>
    public int UseCount(Guid labelId)
    {
        int listed = _store.Lists.Tasks.Count(item => item.Task.IsLive && item.Task.LabelId == labelId);
        if (_tasks.Current.SessionStart is null)
        {
            return listed;
        }

        Guid categoryId = CategoryIdOf(labelId);
        return listed + _tasks.Current.Tasks.Count(task => task.CategoryId == categoryId);
    }

    public TaskLabelNameCheck CheckName(Guid? labelId, string name)
    {
        // A rename sees every label (no two share a name); a new name ignores the ones this device can't see: it joins them.
        return TaskLabelEdits.CheckName(_store.Lists.Labels.Select(item => item.Label), labelId, name, _store.SelfDevice, _store.Removed);
    }

    /// <summary>The first colour of the 12-colour set no label has (what a new label starts with).</summary>
    public string NextColor()
    {
        return TaskLabelColors.FirstUnused(Labels);
    }

    /// <summary>A new label in <paramref name="color"/>, shared unless <paramref name="onlyOnThisDevice"/>; null when the name is blank or taken (nothing added).</summary>
    public async Task<Guid?> AddAsync(string name, string color, bool onlyOnThisDevice = false)
    {
        return (await AddScopedAsync(name, color, onlyOnThisDevice)).Id;
    }

    /// <summary>
    /// <see cref="AddAsync"/> that also says when the name was a device-only label of other devices, which this device joined (ADR 89:
    /// <c>JoinedWith</c> names them, "Laptop, Phone").
    /// </summary>
    /// <param name="groupId">Tasks redesign (Q1): the category the new label goes in (null: none), set in the same stamped change as the add.</param>
    public async Task<(Guid? Id, string? JoinedWith)> AddScopedAsync(string name, string color, bool onlyOnThisDevice, Guid? groupId = null)
    {
        Guid? added = null;
        string? joined = null;
        await _store.ChangeAsync(this, (lists, self, now) =>
        {
            TaskLabel? hidden = lists.Labels.Select(item => item.Label).FirstOrDefault(
                label => label.IsLive && TaskLabel.SameName(label.Name, name.Trim()) && !_store.LabelShown(label));
            TaskLists changed = TaskLabelEdits.Add(lists, name, color, now, self, out Guid? id, onlyOnThisDevice, _store.Removed);
            added = id;
            joined = hidden is null || id is null ? null : _store.OtherDevicesText(hidden.OnlyOn);
            return id is Guid labelId && groupId is Guid group ? TaskLabelEdits.SetGroup(changed, labelId, group, now, self) : changed;
        });
        return (added, joined);
    }

    /// <summary>
    /// Shared: the label shows on every device ("Make shared"; the caller has asked the user). Device-only: only this device and the devices
    /// already naming it (ADR 89: a device is never removed from a list, so a several-device list is joined, not replaced).
    /// </summary>
    public Task SetSharedAsync(Guid labelId, bool shared)
    {
        return _store.ChangeAsync(this, (lists, self, now) =>
        {
            TaskLabel? label = lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.Id == labelId && item.IsLive);
            return label is null
                ? lists
                : TaskLabelEdits.SetOnlyOn(lists, labelId, shared ? null : self == Guid.Empty ? null : TaskLabelEdits.Joined(label.OnlyOn, self), now, self);
        });
    }

    /// <summary>Every tag on the tasks this device shows, once each, in name order.</summary>
    public IReadOnlyList<string> Tags => _store.VisibleTags();

    /// <summary>How many live tasks have the tag.</summary>
    public int TagUseCount(string tag)
    {
        return _store.Lists.Tasks.Count(item => item.Task.IsLive && TaskLabels.Has(item.Task.Tags, tag));
    }

    /// <summary>The tag shows on every device (<paramref name="shared"/>), or only on this one (joined with the devices already naming it).</summary>
    public Task SetTagSharedAsync(string tag, bool shared)
    {
        return _store.ChangeAsync(this, (lists, self, now) => lists with { TagScopes = TagScopeEdits.SetScope(lists.TagScopes, tag, !shared, now, self) });
    }

    /// <summary>Renames the label; a blank or taken name changes nothing and says why.</summary>
    public async Task<TaskLabelNameCheck> RenameAsync(Guid labelId, string name)
    {
        TaskLabelNameCheck check = CheckName(labelId, name);
        if (check == TaskLabelNameCheck.Fine)
        {
            await _tasks.RenameCategoryAsync(CategoryIdOf(labelId), name.Trim());
        }

        return check;
    }

    public Task RecolourAsync(Guid labelId, string color)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskLabelEdits.Recolour(lists, labelId, color, now));
    }

    /// <summary>The devices the label's task reminders ring on (null: every device).</summary>
    public Task SetRingOnAsync(Guid labelId, IReadOnlyList<Guid>? ringOn)
    {
        return _store.ChangeAsync(this, (lists, self, now) => TaskLabelEdits.SetRingOn(lists, labelId, ringOn, now, self));
    }

    /// <summary>The label goes on every device; its tasks (and a running checklist's) are left with No label.</summary>
    public Task DeleteAsync(Guid labelId)
    {
        return _tasks.DeleteCategoryAsync(CategoryIdOf(labelId));
    }

    // ---- ADR 93: label groups ("categories" in the UI), always shared; every edit stamped, so it syncs ----

    /// <summary>Whether <paramref name="name"/> can be a category's (<paramref name="groupId"/>: the one being renamed; null for a new one).</summary>
    public TaskLabelNameCheck CheckGroupName(Guid? groupId, string name)
    {
        return TaskLabelGroupEdits.CheckName(_store.Lists.LabelGroups, groupId, name);
    }

    /// <summary>A new category at the end of the list; null when the name is blank or taken (nothing added).</summary>
    public async Task<Guid?> AddGroupAsync(string name, string icon)
    {
        Guid? added = null;
        await _store.ChangeAsync(this, (lists, _, now) =>
        {
            TaskLists changed = TaskLabelGroupEdits.Add(lists, name, icon, now, out Guid? id);
            added = id;
            return changed;
        });
        return added;
    }

    public Task RenameGroupAsync(Guid groupId, string name)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskLabelGroupEdits.Rename(lists, groupId, name, now));
    }

    public Task SetGroupIconAsync(Guid groupId, string icon)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskLabelGroupEdits.SetIcon(lists, groupId, icon, now));
    }

    /// <summary>The category goes on every device (a tombstone); its labels are never edited for it and show under "Other labels".</summary>
    public Task DeleteGroupAsync(Guid groupId)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskLabelGroupEdits.Delete(lists, groupId, now));
    }

    /// <summary>The categories in <paramref name="order"/> (a stamped position edit of each one that moves).</summary>
    public Task ReorderGroupsAsync(IReadOnlyList<Guid> order)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskLabelGroupEdits.Reorder(lists, order, now));
    }

    /// <summary>
    /// A label dragged to a place: into the category <paramref name="groupId"/> (null: none, "Other labels") if it isn't there (its group
    /// choice stamped on its own), then <paramref name="orderInGroup"/> (that category's labels as shown, the label in its new place) as a
    /// stamped position edit. One change of the task list, so it syncs as one.
    /// </summary>
    public Task MoveLabelAsync(Guid labelId, Guid? groupId, IReadOnlyList<Guid> orderInGroup)
    {
        return _store.ChangeAsync(this, (lists, self, now) =>
        {
            // Only a move to another category edits the group choice: a label under "Other labels" because its category was deleted keeps
            // naming it when it is only reordered there.
            TaskLabel? label = lists.Labels.Select(item => item.Label).FirstOrDefault(item => item.Id == labelId && item.IsLive);
            IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(TaskLabelGroupBook.Live(lists.LabelGroups));
            TaskLists changed = label is not null && TaskLabelGroupBook.GroupOf(label, liveGroups)?.Id != groupId
                ? TaskLabelEdits.SetGroup(lists, labelId, groupId, now, self)
                : lists;
            return TaskLabelEdits.Reorder(changed, orderInGroup, now);
        });
    }

    // The id the label's category has on this PC (its own id unless the migration kept an older one).
    private Guid CategoryIdOf(Guid labelId)
    {
        return _store.CategoryIds.TryGetValue(labelId, out Guid categoryId) ? categoryId : labelId;
    }
}
