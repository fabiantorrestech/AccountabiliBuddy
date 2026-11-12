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

    /// <summary>The live labels, in the list's order.</summary>
    public IReadOnlyList<TaskLabel> Labels => _store.Lists.Labels
        .Select(item => item.Label)
        .Where(label => label.IsLive && label.Name.Trim().Length > 0)
        .OrderBy(label => label.Position).ThenBy(label => label.Id.ToString("D"), StringComparer.Ordinal)
        .ToList();

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
        return TaskLabelEdits.CheckName(Labels, labelId, name);
    }

    /// <summary>The first colour of the 12-colour set no label has (what a new label starts with).</summary>
    public string NextColor()
    {
        return TaskLabelColors.FirstUnused(Labels);
    }

    /// <summary>A new label in <paramref name="color"/>; null when the name is blank or taken (nothing added).</summary>
    public async Task<Guid?> AddAsync(string name, string color)
    {
        Guid? added = null;
        await _store.ChangeAsync(this, (lists, self, now) =>
        {
            TaskLists changed = TaskLabelEdits.Add(lists, name, color, now, self, out Guid? id);
            added = id;
            return changed;
        });
        return added;
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

    // The id the label's category has on this PC (its own id unless the migration kept an older one).
    private Guid CategoryIdOf(Guid labelId)
    {
        return _store.CategoryIds.TryGetValue(labelId, out Guid categoryId) ? categoryId : labelId;
    }
}
