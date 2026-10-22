namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A guided session's checklist (ADR 24, 25): tasks with subtasks, each ticked with the time. A task with subtasks is
/// done when all of them are (at the last one's time); ticking it ticks them all, unticking it unticks them all.
/// </summary>
/// <param name="SessionStart">When its session started; null while it's a draft before Start.</param>
/// <param name="MadeBeforeStart">It had at least one task when the session started (ADR 25's guard).</param>
public sealed record SessionChecklist(DateTimeOffset? SessionStart, bool MadeBeforeStart, IReadOnlyList<SessionTask> Tasks)
{
    /// <summary>How long a session runs before a finished checklist may end it at once (ADR 25).</summary>
    public static readonly TimeSpan EndAtOnceAfter = TimeSpan.FromMinutes(10);

    public static SessionChecklist Empty { get; } = new(null, false, []);

    public bool HasTasks => Tasks.Count > 0;

    public bool AllDone => HasTasks && Tasks.All(task => task.IsDone);

    public int DoneCount => Tasks.Count(task => task.IsDone);

    /// <summary>The first task still open, or null.</summary>
    public SessionTask? Current => Tasks.FirstOrDefault(task => !task.IsDone);

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

    /// <summary>A task (no <paramref name="parentId"/>) or a subtask of that task; a blank title adds nothing.</summary>
    public SessionChecklist Add(string title, Guid? parentId)
    {
        string trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return this;
        }

        if (parentId is not Guid parent)
        {
            return this with { Tasks = [.. Tasks, new SessionTask(Guid.NewGuid(), trimmed, null, [])] };
        }

        // A new open subtask reopens a task that was done.
        return WithTask(parent, task => task with { DoneAt = null, Subtasks = [.. task.Subtasks, new SessionSubtask(Guid.NewGuid(), trimmed, null)] });
    }

    /// <summary>Ticks or unticks the task or subtask <paramref name="id"/> at <paramref name="now"/>.</summary>
    public SessionChecklist Toggle(Guid id, DateTimeOffset now)
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
        return this with
        {
            Tasks = Tasks
                .Where(task => task.Id != id)
                .Select(task => task.Subtasks.Any(subtask => subtask.Id == id)
                    ? task with { Subtasks = task.Subtasks.Where(subtask => subtask.Id != id).ToList() }
                    : task)
                .ToList(),
        };
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

    private SessionChecklist WithTask(Guid id, Func<SessionTask, SessionTask> change)
    {
        return this with { Tasks = Tasks.Select(task => task.Id == id ? change(task) : task).ToList() };
    }
}
