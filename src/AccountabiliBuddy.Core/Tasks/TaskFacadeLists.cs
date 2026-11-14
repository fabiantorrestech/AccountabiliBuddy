using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The PC's own old lists (the next session's list, Later, come-backs, Auto-saved, categories) as views of the one task list
/// (ADR 76), so the screens built on them keep working unchanged while the task list is the only thing stored. Unlike the old sync
/// fields (<see cref="OldTaskFieldsWriter"/>) these show what the PC showed before: a ticked task stays in the next session's list
/// whatever day it was ticked, History's finished Later items and come-backs show ticked in their list, and a local item kept
/// here keeps its text. No tombstones: a change the PC makes to them goes back through <see cref="TaskListRules.ApplyLocal"/>.
/// Positions are the old lists' (<see cref="OldTaskFieldsWriter.OldPosition"/>), so the order the user sees is the order kept.
/// </summary>
public static class TaskFacadeLists
{
    /// <summary>The old list a live task shows in on this PC; a finished Shelf task (on its way to History) shows in its Shelf bin's list.</summary>
    public static OldTaskList ListOf(TaskItem task, DateTimeOffset now)
    {
        return OldTaskFieldsWriter.ListOf(task with { Bin = ShownBin(task) }, now);
    }

    /// <summary>The bin whose place in its old list a task takes: a finished Shelf task's own Shelf bin, else its bin.</summary>
    public static TaskBin ShownBin(TaskItem task)
    {
        return task.Bin == TaskBin.Done && task.PrevBin is TaskBin.Sort or TaskBin.Next or TaskBin.Later ? task.PrevBin.Value : task.Bin;
    }

    /// <summary>The next session's list: This session, Queued and Finished (ticked ones of any day), in order.</summary>
    public static List<SyncNextTask> Draft(TaskLists lists, DateTimeOffset now, Func<TaskLabel, bool>? labelShown = null, Func<string, bool>? tagShown = null)
    {
        List<TaskLabel> labels = LabelsOf(lists, labelShown);
        return LiveIn(lists, OldTaskList.NextTasks, now)
            .Select(task => OldTaskFieldsWriter.ToNextTask(task with { Tags = ShownTags(task.Tags, tagShown) }, labels))
            .OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Later and Next (marked), with History's finished ones of the last 90 days ticked.</summary>
    public static List<SyncLaterItem> Later(TaskLists lists, DateTimeOffset now, Func<TaskLabel, bool>? labelShown = null, Func<string, bool>? tagShown = null)
    {
        List<TaskLabel> labels = LabelsOf(lists, labelShown);
        return LiveIn(lists, OldTaskList.Later, now).Select(task => OldTaskFieldsWriter.ToLaterItem(Shown(task) with { Tags = ShownTags(task.Tags, tagShown) }, labels))
            .Concat(Finished(lists, OldTaskList.Later, now).Select(task => OldTaskFieldsWriter.ToLaterItem(task with { Tags = ShownTags(task.Tags, tagShown) }, labels)))
            .OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The come-backs: To sort (except Auto-saved's, within its 30 days), with History's finished ones of the last 90 days ticked.</summary>
    public static List<SyncParked> Parked(TaskLists lists, DateTimeOffset now, Func<TaskLabel, bool>? labelShown = null, Func<string, bool>? tagShown = null)
    {
        List<TaskLabel> labels = LabelsOf(lists, labelShown);
        return LiveIn(lists, OldTaskList.Parked, now).Select(task => OldTaskFieldsWriter.ToParked(Shown(task) with { Tags = ShownTags(task.Tags, tagShown) }, labels))
            .Concat(Finished(lists, OldTaskList.Parked, now).Select(task => OldTaskFieldsWriter.ToParked(task with { Tags = ShownTags(task.Tags, tagShown) }, labels)))
            .OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Auto-saved: the open To sort tasks that came from it, within its 30 days.</summary>
    public static List<SyncAutoSavedItem> AutoSaved(TaskLists lists, DateTimeOffset now, Func<TaskLabel, bool>? labelShown = null, Func<string, bool>? tagShown = null)
    {
        List<TaskLabel> labels = LabelsOf(lists, labelShown);
        return LiveIn(lists, OldTaskList.AutoSaved, now)
            .Select(task => OldTaskFieldsWriter.ToAutoSaved(task with { Tags = ShownTags(task.Tags, tagShown) }, labels))
            .OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The labels as the PC's task categories, in order. A label keeps the id its category had on this PC
    /// (<paramref name="categoryIds"/>: label id to old category id), so checklists and filters that name it still find it.
    /// </summary>
    public static List<TaskCategory> Categories(IReadOnlyList<SyncTaskLabel> labels, IReadOnlyDictionary<Guid, Guid> categoryIds, Func<TaskLabel, bool>? labelShown = null)
    {
        return labels
            .Select(item => item.Label)
            .Where(label => label.IsLive && label.Name.Trim().Length > 0 && (labelShown?.Invoke(label) ?? true))
            .OrderBy(label => label.Position).ThenBy(label => label.Id.ToString("D"), StringComparer.Ordinal)
            .Select(label => new TaskCategory(
                categoryIds.TryGetValue(label.Id, out Guid categoryId) ? categoryId : label.Id,
                label.Name,
                TaskLabelColors.Opaque(label.Color),
                label.EditedAt > GuidanceState.UpgradeStamp ? label.EditedAt : null))
            .ToList();
    }

    // The labels a screen shows (ADR 89): a task whose label isn't among them reads as No label; the label stays on the task.
    private static List<TaskLabel> LabelsOf(TaskLists lists, Func<TaskLabel, bool>? labelShown = null)
    {
        return lists.Labels.Select(item => item.Label).Where(label => labelShown?.Invoke(label) ?? true).ToList();
    }

    private static IReadOnlyList<string>? ShownTags(IReadOnlyList<string>? tags, Func<string, bool>? tagShown)
    {
        return tagShown is null || tags is null ? tags : TaskLabels.Clean(tags.Where(tagShown));
    }

    private static IEnumerable<TaskItem> LiveIn(TaskLists lists, OldTaskList list, DateTimeOffset now)
    {
        return lists.Tasks.Select(item => item.Task).Where(task => task.IsLive && ListOf(task, now) == list);
    }

    // A finished Shelf task not yet in History shows ticked in its Shelf list, at its place there.
    private static TaskItem Shown(TaskItem task)
    {
        TaskBin shown = ShownBin(task);
        return shown == task.Bin ? task : task with { Bin = shown };
    }

    private static IEnumerable<TaskItem> Finished(TaskLists lists, OldTaskList list, DateTimeOffset now)
    {
        HashSet<Guid> live = lists.Tasks.Where(item => item.Task.IsLive).Select(item => item.Id).ToHashSet();
        return lists.Log
            .Select(item => item.Entry)
            .Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Finished && entry.Task.DoneAt is not null
                && now - entry.When < OldTaskFieldsWriter.FinishedShown && !live.Contains(entry.Id))
            .Select(entry => entry.Task with
            {
                Id = entry.Id,
                Bin = entry.Task.PrevBin ?? TaskBin.Later,
                // An Undo or untick must be newer than the entry, not only than the task it holds.
                EditedAt = entry.EditedAt > entry.Task.EditedAt ? entry.EditedAt : entry.Task.EditedAt,
                DeletedAt = null,
            })
            .Where(task => OldTaskFieldsWriter.ListOf(task.Bin) == list);
    }
}
