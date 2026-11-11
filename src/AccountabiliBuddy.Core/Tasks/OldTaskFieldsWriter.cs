using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Rebuilds the old sync fields from the task list (ADR 76), for apps older than it, on every write; they are never stored on
/// their own. Each task travels in its bin's old list (<see cref="ListOf"/>) with what that list can carry; a deleted task as a
/// tombstone there. A task that changed bins in the last 30 days also leaves a tombstone in the other old lists, so an older app
/// takes it out of the list it was in. Next goes into <c>later</c> with the <see cref="SyncLaterItem.NextBin"/> marker. Positions
/// keep each bin's tasks together in the shared old list (<see cref="OldPosition"/>). Labels go as categories with an opaque colour.
/// An open To sort task from Auto-saved goes in <c>autoSaved</c> for its 30 days, then in <c>parked</c>, never in both.
/// </summary>
public static class OldTaskFieldsWriter
{
    /// <summary>How far a later bin's positions are moved in an old list that holds two or three bins.</summary>
    public const double BinSpacing = 1_000_000;

    /// <summary>How long History's finished tasks still show ticked in <c>later</c> and <c>parked</c>, as ticked Later items did.</summary>
    public static readonly TimeSpan FinishedShown = TimeSpan.FromDays(90);

    /// <summary>
    /// The old list a task travels in at <paramref name="now"/>: its bin's, except an open To sort task that came from Auto-saved and
    /// is within Auto-saved's 30 days, which travels in <c>autoSaved</c> (and only there).
    /// </summary>
    public static OldTaskList ListOf(TaskItem task, DateTimeOffset now)
    {
        bool autoSavedHome = task.Bin == TaskBin.Sort && task.AutoSavedFor is not null && task.DoneAt is null
            && now - task.CreatedAt < SyncAutoSavedItem.Kept;
        return autoSavedHome ? OldTaskList.AutoSaved : ListOf(task.Bin);
    }

    /// <summary>Whether two old lists are the same bin's (<c>parked</c> and <c>autoSaved</c> both hold To sort).</summary>
    public static bool SameHome(OldTaskList first, OldTaskList second)
    {
        return first == second || (IsSortList(first) && IsSortList(second));
    }

    public static OldTaskList ListOf(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Session or TaskBin.Queued or TaskBin.Done => OldTaskList.NextTasks,
            TaskBin.Next or TaskBin.Later => OldTaskList.Later,
            _ => OldTaskList.Parked,
        };
    }

    /// <summary>A bin's position in its old list: Queued after This session, Finished after Queued; Later after Next.</summary>
    public static double OldPosition(TaskBin bin, double position)
    {
        return bin switch
        {
            TaskBin.Queued or TaskBin.Later => position + BinSpacing,
            TaskBin.Done => position + 2 * BinSpacing,
            _ => position,
        };
    }

    /// <summary>The inverse of <see cref="OldPosition"/>.</summary>
    public static double NewPosition(TaskBin bin, double oldPosition)
    {
        return bin switch
        {
            TaskBin.Queued or TaskBin.Later => oldPosition - BinSpacing,
            TaskBin.Done => oldPosition - 2 * BinSpacing,
            _ => oldPosition,
        };
    }

    /// <param name="stubLocalItems">Write a task kept on one device as ADR 72's stub (the sync file); false keeps its text (a backup).</param>
    public static OldTaskFields Write(
        IReadOnlyList<SyncTaskItem> tasks,
        IReadOnlyList<SyncTaskLabel> labels,
        IReadOnlyList<SyncTaskLogItem> history,
        DateTimeOffset now,
        TimeZoneInfo zone,
        bool stubLocalItems = true)
    {
        List<TaskLabel> labelList = labels.Select(item => item.Label).ToList();
        DateOnly today = LocalDate(now, zone);
        var next = new ListBuilder<SyncNextTask>();
        var later = new ListBuilder<SyncLaterItem>();
        var parked = new ListBuilder<SyncParked>();
        var autoSaved = new ListBuilder<SyncAutoSavedItem>();

        foreach (SyncTaskItem item in tasks)
        {
            TaskItem task = item.Task;
            OldTaskList list = task.IsLive ? ListOf(task, now) : ListOf(task.Bin);
            if (!task.IsLive)
            {
                AddTombstone(list, task.Id, task.DeletedAt ?? task.EditedAt, task.LocalTo, task.CreatedAt);
                if (task.Bin == TaskBin.Sort && task.AutoSavedFor is not null)
                {
                    // An older app may still hold it under Auto-saved.
                    AddTombstone(OldTaskList.AutoSaved, task.Id, task.DeletedAt ?? task.EditedAt, task.LocalTo, task.CreatedAt);
                }

                continue;
            }

            if (task.Bin == TaskBin.Done && (task.DoneAt is not DateTimeOffset doneAt || LocalDate(doneAt, zone) != today))
            {
                // Finished before today: on its way to History; older apps keep the copy they have.
                continue;
            }

            switch (list)
            {
                case OldTaskList.NextTasks:
                    next.Add(ToNextTask(task, labelList));
                    break;
                case OldTaskList.Later:
                    later.Add(ToLaterItem(task, labelList));
                    break;
                case OldTaskList.AutoSaved:
                    autoSaved.Add(ToAutoSaved(task, labelList));
                    break;
                default:
                    parked.Add(ToParked(task, labelList));
                    break;
            }

            if (item.Stamps.Bin is TaskFieldStamp moved && now - moved.At < SharedListMerge.TombstonesKept)
            {
                foreach (OldTaskList other in Enum.GetValues<OldTaskList>().Where(other => other != list))
                {
                    AddTombstone(other, task.Id, moved.At, null, task.CreatedAt);
                }
            }
        }

        foreach (TaskHistoryEntry entry in history.Select(item => item.Entry).Where(entry => entry.IsLive && entry.Kind == TaskHistoryKind.Finished))
        {
            if (now - entry.When >= FinishedShown || entry.Task.DoneAt is null)
            {
                continue;
            }

            TaskItem task = entry.Task with { Bin = entry.Task.PrevBin ?? TaskBin.Later };
            switch (ListOf(task.Bin))
            {
                case OldTaskList.Later:
                    later.Add(ToLaterItem(task, labelList));
                    break;
                case OldTaskList.Parked:
                    parked.Add(ToParked(task, labelList));
                    break;
            }
        }

        List<SyncComeback> comebacks = tasks
            .Select(item => item.Task)
            .Where(task => task.IsLive && ListOf(task, now) == OldTaskList.Parked && task.DoneAt is null && task.Devices is null)
            .OrderBy(task => task.Position)
            .Select(task => new SyncComeback(task.Id, task.Title, task.CreatedAt))
            .ToList();
        List<SyncCategory> categories = labelList
            .Where(label => label.IsLive)
            .OrderBy(label => label.Position)
            .Select(label => new SyncCategory(label.Name, TaskLabelColors.Opaque(label.Color), label.EditedAt))
            .ToList();

        return new OldTaskFields(
            Finish(next.Items, stubLocalItems),
            Finish(later.Items, stubLocalItems),
            Finish(parked.Items, stubLocalItems),
            comebacks,
            categories,
            Finish(autoSaved.Items, stubLocalItems));

        void AddTombstone(OldTaskList list, Guid id, DateTimeOffset at, Guid? localTo, DateTimeOffset createdAt)
        {
            switch (list)
            {
                case OldTaskList.NextTasks:
                    next.Add(new SyncNextTask(id, string.Empty, null, null, null, null, at, at, 0, LocalTo: localTo));
                    break;
                case OldTaskList.Later:
                    later.Add(new SyncLaterItem(id, string.Empty, null, null, null, null, null, null, at, at, 0, LocalTo: localTo));
                    break;
                case OldTaskList.AutoSaved:
                    autoSaved.Add(new SyncAutoSavedItem(id, string.Empty, DateOnly.FromDateTime(createdAt.UtcDateTime), createdAt, at, at, 0, LocalTo: localTo));
                    break;
                default:
                    parked.Add(new SyncParked(id, string.Empty, createdAt, null, at, at, 0, LocalTo: localTo));
                    break;
            }
        }
    }

    public static SyncNextTask ToNextTask(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        TaskLabel? label = LabelOf(task, labels);
        return new SyncNextTask(
            task.Id,
            task.Title,
            task.DoneAt,
            label?.Name,
            label is null ? null : TaskLabelColors.Opaque(label.Color),
            SubtasksOf(task),
            task.EditedAt,
            null,
            OldPosition(task.Bin, task.Position),
            task.EstimateMinutes,
            task.Note,
            task.PlannedFor,
            TaskLabels.Clean(task.Tags),
            task.LocalTo);
    }

    public static SyncLaterItem ToLaterItem(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        TaskLabel? label = LabelOf(task, labels);
        return new SyncLaterItem(
            task.Id,
            task.Title,
            SubtasksOf(task),
            label?.Name,
            label is null ? null : TaskLabelColors.Opaque(label.Color),
            task.EstimateMinutes,
            task.Note,
            task.DueOn,
            task.EditedAt,
            null,
            OldPosition(task.Bin, task.Position),
            task.PlannedFor,
            task.DoneAt,
            TaskLabels.Clean(task.Tags),
            task.LocalTo,
            task.Bin == TaskBin.Next ? SyncLaterItem.NextBin : null);
    }

    public static SyncParked ToParked(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        TaskLabel? label = LabelOf(task, labels);
        return new SyncParked(
            task.Id,
            task.Title,
            task.CreatedAt,
            task.DoneAt,
            task.EditedAt,
            null,
            OldPosition(task.Bin, task.Position),
            label?.Name,
            label is null ? null : TaskLabelColors.Opaque(label.Color),
            TaskLabels.Clean(task.Tags),
            task.LocalTo);
    }

    /// <summary>The task as an Auto-saved item: saved at its creation, for its plan day.</summary>
    public static SyncAutoSavedItem ToAutoSaved(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        TaskLabel? label = LabelOf(task, labels);
        return new SyncAutoSavedItem(
            task.Id,
            task.Title,
            task.AutoSavedFor ?? DateOnly.FromDateTime(task.CreatedAt.UtcDateTime),
            task.CreatedAt,
            task.EditedAt,
            null,
            OldPosition(task.Bin, task.Position),
            label?.Name,
            label is null ? null : TaskLabelColors.Opaque(label.Color),
            TaskLabels.Clean(task.Tags),
            task.LocalTo);
    }

    private static bool IsSortList(OldTaskList list)
    {
        return list is OldTaskList.Parked or OldTaskList.AutoSaved;
    }

    private static IReadOnlyList<SessionSubtask>? SubtasksOf(TaskItem task)
    {
        return task.Steps is { Count: > 0 } steps ? steps.Select(step => step.ToSubtask()).ToList() : null;
    }

    private static TaskLabel? LabelOf(TaskItem task, IReadOnlyList<TaskLabel> labels)
    {
        return task.LabelId is Guid id ? labels.FirstOrDefault(label => label.Id == id && label.IsLive) : null;
    }

    private static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    }

    private static List<T> Finish<T>(IEnumerable<T> items, bool stubLocalItems)
        where T : ISharedItem
    {
        List<T> ordered = items.OrderBy(item => item.Position).ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal).ToList();
        return stubLocalItems ? SharedListMerge.ForFile(ordered) : ordered;
    }

    // One entry per id in an old list: a live one over a tombstone, then the newest.
    private sealed class ListBuilder<T>
        where T : ISharedItem
    {
        private readonly Dictionary<Guid, T> _byId = new();

        public IEnumerable<T> Items => _byId.Values;

        public void Add(T item)
        {
            if (!_byId.TryGetValue(item.Id, out T? held) || Better(item, held))
            {
                _byId[item.Id] = item;
            }
        }

        private static bool Better(T candidate, T held)
        {
            bool candidateLive = candidate.DeletedAt is null;
            bool heldLive = held.DeletedAt is null;
            if (candidateLive != heldLive)
            {
                return candidateLive;
            }

            return candidate.EditedAt > held.EditedAt;
        }
    }
}
