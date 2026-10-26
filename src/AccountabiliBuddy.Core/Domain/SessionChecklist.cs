namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A guided session's checklist (ADR 24, 25): tasks with subtasks, each ticked with the time. A task with subtasks is
/// done when all of them are (at the last one's time); ticking it ticks them all, unticking it unticks them all.
/// </summary>
/// <param name="SessionStart">When its session started; null while it's a draft before Start.</param>
/// <param name="MadeBeforeStart">It had at least one task when the session started (ADR 25's guard).</param>
/// <param name="CurrentId">The open task or subtask picked to show (on the on-screen window's bar); null = the first open task.</param>
/// <param name="DroppedOpen">
/// Open tasks removed or parked while the session runs. Above 0 the checklist is never "all done", so taking tasks off
/// can't end a session at once (ADR 25): ticking is the only way to done. Stored with the checklist; 0 when a session starts.
/// </param>
public sealed record SessionChecklist(
    DateTimeOffset? SessionStart,
    bool MadeBeforeStart,
    IReadOnlyList<SessionTask> Tasks,
    Guid? CurrentId = null,
    int DroppedOpen = 0)
{
    /// <summary>How long a session runs before a finished checklist may end it at once (ADR 25).</summary>
    public static readonly TimeSpan EndAtOnceAfter = TimeSpan.FromMinutes(10);

    public static SessionChecklist Empty { get; } = new(null, false, []);

    public bool HasTasks => Tasks.Count > 0;

    /// <summary>Every task ticked, and none taken off open while the session ran (<see cref="DroppedOpen"/>).</summary>
    public bool AllDone => HasTasks && DroppedOpen == 0 && Tasks.All(task => task.IsDone);

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
        return this with { SessionStart = at, MadeBeforeStart = HasTasks, DroppedOpen = 0 };
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

    /// <summary>
    /// A task put first: in the first open task's category (none when it has none, or the list is empty), at the top of
    /// the list, so it stays first once grouped. A blank title adds nothing.
    /// </summary>
    public SessionChecklist AddFirst(string title)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return this;
        }

        Guid? categoryId = Tasks.FirstOrDefault(task => !task.IsDone)?.CategoryId;
        return this with { Tasks = [new SessionTask(Guid.NewGuid(), trimmed, null, [], categoryId), .. Tasks] };
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
    /// Drag and drop: puts the task <paramref name="id"/> just before (or <paramref name="after"/>) the task
    /// <paramref name="targetId"/>, in that task's category, so a task dropped among another category's tasks joins it;
    /// or a subtask just before or after another subtask of the same task. Anything else changes nothing.
    /// </summary>
    public SessionChecklist MoveTo(Guid id, Guid targetId, bool after)
    {
        if (id == targetId)
        {
            return this;
        }

        SessionTask? moved = Tasks.FirstOrDefault(task => task.Id == id);
        SessionTask? target = Tasks.FirstOrDefault(task => task.Id == targetId);
        if (moved is not null)
        {
            if (target is null)
            {
                return this;
            }

            List<SessionTask> recategorized = Tasks.Select(task => task.Id == id ? task with { CategoryId = target.CategoryId } : task).ToList();
            return this with { Tasks = Placed(recategorized, task => task.Id == id, task => task.Id == targetId, after) };
        }

        return this with
        {
            Tasks = Tasks.Select(task => task.Subtasks.Any(subtask => subtask.Id == id) && task.Subtasks.Any(subtask => subtask.Id == targetId)
                ? task with { Subtasks = Placed(task.Subtasks, subtask => subtask.Id == id, subtask => subtask.Id == targetId, after) }
                : task).ToList(),
        };
    }

    /// <summary>
    /// What parking the task <paramref name="taskId"/> leaves as a come-back: its title, plus " (+N steps)" when it has
    /// open subtasks. Null for an unknown task.
    /// </summary>
    public string? ParkText(Guid taskId)
    {
        SessionTask? task = Tasks.FirstOrDefault(task => task.Id == taskId);
        if (task is null)
        {
            return null;
        }

        int openSteps = task.Subtasks.Count(subtask => !subtask.IsDone);
        return openSteps > 0 ? $"{task.Title} (+{openSteps} {(openSteps == 1 ? "step" : "steps")})" : task.Title;
    }

    /// <summary>
    /// The checklist without the parked task <paramref name="taskId"/>. When the bar showed it, the bar moves on to the
    /// next open task after it (else the first open one).
    /// </summary>
    public SessionChecklist Parked(Guid taskId)
    {
        int index = Tasks.ToList().FindIndex(task => task.Id == taskId);
        if (index < 0)
        {
            return this;
        }

        bool wasShown = Shown?.Task.Id == taskId;
        SessionTask? next = Tasks.Skip(index + 1).FirstOrDefault(task => !task.IsDone);
        SessionChecklist removed = Remove(taskId);
        return wasShown ? removed with { CurrentId = next?.Id } : removed;
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

    /// <summary>
    /// Takes a task or subtask off. An open task taken off (removed or parked) once the session runs counts in
    /// <see cref="DroppedOpen"/>, so it never makes the checklist "all done" (ADR 25).
    /// </summary>
    public SessionChecklist Remove(Guid id)
    {
        bool dropsOpenTask = SessionStart is not null && Tasks.Any(task => task.Id == id && !task.IsDone);
        SessionChecklist removed = this with
        {
            DroppedOpen = dropsOpenTask ? DroppedOpen + 1 : DroppedOpen,
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

    // The item taken out and put back just before (or after) the target; unchanged when either isn't there.
    private static List<T> Placed<T>(IReadOnlyList<T> items, Func<T, bool> isIt, Func<T, bool> isTarget, bool after)
    {
        var list = items.ToList();
        int from = list.FindIndex(item => isIt(item));
        if (from < 0 || list.FindIndex(item => isTarget(item)) < 0)
        {
            return list;
        }

        T item = list[from];
        list.RemoveAt(from);
        int to = list.FindIndex(other => isTarget(other));
        list.Insert(after ? to + 1 : to, item);
        return list;
    }

    private SessionChecklist WithTask(Guid id, Func<SessionTask, SessionTask> change)
    {
        return this with { Tasks = Tasks.Select(task => task.Id == id ? change(task) : task).ToList() };
    }
}
