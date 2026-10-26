namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A guided session's checklist (ADR 24, 25): tasks with subtasks, each ticked with the time. A task with subtasks is
/// done when all of them are (at the last one's time); ticking it ticks them all, unticking it unticks them all.
/// </summary>
/// <param name="SessionStart">When its session started; null while it's a draft before Start.</param>
/// <param name="MadeBeforeStart">It had at least one task when the session started (ADR 25's guard).</param>
/// <param name="CurrentId">The open task or subtask picked to show (on the on-screen window's bar); null = the first open task.</param>
public sealed record SessionChecklist(DateTimeOffset? SessionStart, bool MadeBeforeStart, IReadOnlyList<SessionTask> Tasks, Guid? CurrentId = null)
{
    /// <summary>How long a session runs before a finished checklist may end it at once (ADR 25).</summary>
    public static readonly TimeSpan EndAtOnceAfter = TimeSpan.FromMinutes(10);

    public static SessionChecklist Empty { get; } = new(null, false, []);

    public bool HasTasks => Tasks.Count > 0;

    public bool AllDone => HasTasks && Tasks.All(task => task.IsDone);

    public int DoneCount => Tasks.Count(task => task.IsDone);

    /// <summary>The task shown as current: the picked one (or the picked subtask's), else the first task still open.</summary>
    public SessionTask? Current => Shown?.Task;

    /// <summary>
    /// What the bar shows: the picked open subtask with its task; or the picked open task, else the first open task,
    /// with its first open subtask (null when it has none). Null when every task is done.
    /// </summary>
    public ShownTask? Shown
    {
        get
        {
            foreach (SessionTask task in Tasks.Where(task => !task.IsDone))
            {
                if (task.Subtasks.FirstOrDefault(subtask => subtask.Id == CurrentId && !subtask.IsDone) is SessionSubtask picked)
                {
                    return new ShownTask(task, picked);
                }
            }

            SessionTask? current = Tasks.FirstOrDefault(task => task.Id == CurrentId && !task.IsDone) ?? Tasks.FirstOrDefault(task => !task.IsDone);
            return current is null ? null : new ShownTask(current, current.Subtasks.FirstOrDefault(subtask => !subtask.IsDone));
        }
    }

    /// <summary>The checklist as its session starts at <paramref name="at"/>.</summary>
    public SessionChecklist Started(DateTimeOffset at)
    {
        return this with { SessionStart = at, MadeBeforeStart = HasTasks };
    }

    /// <summary>
    /// ADR 25: every task done, the checklist had tasks when the session started, and the session has run at least
    /// <see cref="EndAtOnceAfter"/>.
    /// </summary>
    public bool CanEndAtOnce(DateTimeOffset now)
    {
        return AllDone && MadeBeforeStart && SessionStart is DateTimeOffset start && now - start >= EndAtOnceAfter;
    }

    /// <summary>
    /// A task (no <paramref name="parentId"/>) in <paramref name="categoryId"/> (null: no category), or a subtask of that
    /// task; a blank title adds nothing.
    /// </summary>
    public SessionChecklist Add(string title, Guid? parentId, Guid? categoryId = null)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return this;
        }

        if (parentId is not Guid parent)
        {
            return this with { Tasks = [.. Tasks, new SessionTask(Guid.NewGuid(), trimmed, null, [], categoryId)] };
        }

        // A new open subtask reopens a task that was done.
        return WithTask(parent, task => task with { DoneAt = null, Subtasks = [.. task.Subtasks, new SessionSubtask(Guid.NewGuid(), trimmed, null)] });
    }

    /// <summary>Puts the task <paramref name="taskId"/> in <paramref name="categoryId"/> (null: no category).</summary>
    public SessionChecklist SetCategory(Guid taskId, Guid? categoryId)
    {
        return WithTask(taskId, task => task with { CategoryId = categoryId });
    }

    /// <summary>The tasks that were in the deleted category <paramref name="categoryId"/> have no category now.</summary>
    public SessionChecklist WithoutCategory(Guid categoryId)
    {
        return this with
        {
            Tasks = Tasks.Select(task => task.CategoryId == categoryId ? task with { CategoryId = null } : task).ToList(),
        };
    }

    /// <summary>
    /// The tasks in the order the checklist shows them: grouped by category in <paramref name="categories"/>' order, no
    /// category (or one not in it) last, each group keeping its own order. Moving a task then moves it within its group.
    /// </summary>
    public SessionChecklist Grouped(IReadOnlyList<TaskCategory> categories)
    {
        int Rank(SessionTask task)
        {
            int index = task.CategoryId is Guid id ? categories.ToList().FindIndex(category => category.Id == id) : -1;
            return index < 0 ? int.MaxValue : index;
        }

        // OrderBy is stable, so each group keeps its order.
        return this with { Tasks = Tasks.OrderBy(Rank).ToList() };
    }

    /// <summary>
    /// The tasks with their category's name and colour filled in (<see cref="SessionTask.Category"/>,
    /// <see cref="SessionTask.CategoryColor"/>), for paired devices and history. A task whose category is gone keeps the
    /// name and colour it was labelled with before; one with no category has neither.
    /// </summary>
    public IReadOnlyList<SessionTask> Labelled(IReadOnlyList<TaskCategory> categories)
    {
        return Tasks.Select(task =>
        {
            if (task.CategoryId is not Guid id)
            {
                return task with { Category = null, CategoryColor = null };
            }

            TaskCategory? category = categories.FirstOrDefault(category => category.Id == id);
            return category is null ? task : task with { Category = category.Name, CategoryColor = category.Color };
        }).ToList();
    }

    /// <summary>
    /// Shows the open task or subtask <paramref name="id"/> as current; picking the current pick again goes back to the
    /// first open task. A done or unknown one isn't picked.
    /// </summary>
    public SessionChecklist Pick(Guid id)
    {
        if (id == CurrentId)
        {
            return this with { CurrentId = null };
        }

        return IsOpen(id) ? this with { CurrentId = id } : this;
    }

    /// <summary>Moves a task among the tasks, or a subtask within its task, by <paramref name="offset"/> places (stops at the ends).</summary>
    public SessionChecklist Move(Guid id, int offset)
    {
        if (Tasks.Any(task => task.Id == id))
        {
            return this with { Tasks = Moved(Tasks, task => task.Id == id, offset) };
        }

        return this with
        {
            Tasks = Tasks.Select(task => task.Subtasks.Any(subtask => subtask.Id == id)
                ? task with { Subtasks = Moved(task.Subtasks, subtask => subtask.Id == id, offset) }
                : task).ToList(),
        };
    }

    /// <summary>
    /// Ticks or unticks the task or subtask <paramref name="id"/> at <paramref name="now"/>. A picked subtask that is done
    /// after it hands the pick to its task's next open subtask (after it, else the first); any other done pick is cleared.
    /// </summary>
    public SessionChecklist Toggle(Guid id, DateTimeOffset now)
    {
        SessionChecklist toggled = Toggled(id, now);
        if (toggled.CurrentId is not Guid picked || toggled.IsOpen(picked))
        {
            return toggled;
        }

        SessionTask? task = toggled.Tasks.FirstOrDefault(task => !task.IsDone && task.Subtasks.Any(subtask => subtask.Id == picked));
        if (task is null)
        {
            return toggled with { CurrentId = null };
        }

        int at = task.Subtasks.ToList().FindIndex(subtask => subtask.Id == picked);
        SessionSubtask? next = task.Subtasks.Skip(at + 1).FirstOrDefault(subtask => !subtask.IsDone)
                               ?? task.Subtasks.FirstOrDefault(subtask => !subtask.IsDone);
        return toggled with { CurrentId = next?.Id };
    }

    private SessionChecklist Toggled(Guid id, DateTimeOffset now)
    {
        return this with
        {
            Tasks = Tasks.Select(task =>
            {
                if (task.Id == id)
                {
                    DateTimeOffset? doneAt = task.IsDone ? null : now;
                    return task with
                    {
                        DoneAt = doneAt,
                        Subtasks = task.Subtasks.Select(subtask => subtask with { DoneAt = doneAt is null ? null : subtask.DoneAt ?? doneAt }).ToList(),
                    };
                }

                if (task.Subtasks.All(subtask => subtask.Id != id))
                {
                    return task;
                }

                List<SessionSubtask> subtasks = task.Subtasks
                    .Select(subtask => subtask.Id == id ? subtask with { DoneAt = subtask.IsDone ? null : now } : subtask)
                    .ToList();
                DateTimeOffset? allDoneAt = subtasks.All(subtask => subtask.IsDone) ? subtasks.Max(subtask => subtask.DoneAt) : null;
                return task with { DoneAt = allDoneAt, Subtasks = subtasks };
            }).ToList(),
        };
    }

    public SessionChecklist Remove(Guid id)
    {
        SessionChecklist removed = this with
        {
            Tasks = Tasks
                .Where(task => task.Id != id)
                .Select(task => task.Subtasks.Any(subtask => subtask.Id == id)
                    ? task with { Subtasks = task.Subtasks.Where(subtask => subtask.Id != id).ToList() }
                    : task)
                .ToList(),
        };
        return removed.CurrentId is Guid picked && !removed.IsOpen(picked) ? removed with { CurrentId = null } : removed;
    }

    public SessionChecklist Rename(Guid id, string title)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return this;
        }

        return this with
        {
            Tasks = Tasks.Select(task => task.Id == id
                ? task with { Title = trimmed }
                : task with { Subtasks = task.Subtasks.Select(subtask => subtask.Id == id ? subtask with { Title = trimmed } : subtask).ToList() })
                .ToList(),
        };
    }

    private bool IsOpen(Guid id)
    {
        return Tasks.Any(task => !task.IsDone && (task.Id == id || task.Subtasks.Any(subtask => subtask.Id == id && !subtask.IsDone)));
    }

    private static List<T> Moved<T>(IReadOnlyList<T> items, Func<T, bool> isIt, int offset)
    {
        var list = items.ToList();
        int from = list.FindIndex(item => isIt(item));
        if (from < 0)
        {
            return list;
        }

        T item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(from + offset, 0, list.Count), item);
        return list;
    }

    private SessionChecklist WithTask(Guid id, Func<SessionTask, SessionTask> change)
    {
        return this with { Tasks = Tasks.Select(task => task.Id == id ? change(task) : task).ToList() };
    }
}
