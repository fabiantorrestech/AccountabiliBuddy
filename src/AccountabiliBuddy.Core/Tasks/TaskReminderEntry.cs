using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One occurrence of a task's reminder as it stands (ADR 76): which task and reminder, when it was due, what happened to it, and
/// when it rings next. Holds no task text, so it is safe to log by its ids and times. A reminder source other than the task list
/// (round 2 R5.5: goal reminders) rings through the same queue with <see cref="Origin"/> set: its <paramref name="TaskId"/> is then
/// that source's item (the goal), never a task.
/// </summary>
/// <param name="TaskId">The task; for another <see cref="Origin"/>, that source's item (a goal).</param>
/// <param name="Due">When the occurrence was due (its identity: a changed due date makes a new, fresh occurrence).</param>
/// <param name="RingAt">When it rings: <paramref name="Due"/>, or the end of a snooze.</param>
public sealed record TaskReminderEntry(
    Guid TaskId,
    Guid ReminderId,
    string Rule,
    DateTimeOffset Due,
    TaskReminderStatus Status,
    DateTimeOffset RingAt,
    int SnoozeCount,
    bool CantMiss)
{
    /// <summary>Where it comes from: a task (the default) or another reminder source (a goal).</summary>
    public TaskReminderOrigin Origin { get; init; } = TaskReminderOrigin.Task;

    /// <summary>Dismissed or started (on any device): it does not ring again.</summary>
    public bool Handled => Status is TaskReminderStatus.Dismissed or TaskReminderStatus.Started;

    /// <summary>Which occurrence this is, for remembering it (never shown). A task's key is unchanged; another source's starts with its origin.</summary>
    public string Key => Origin == TaskReminderOrigin.Task
        ? $"{ReminderId:N}:{Due.UtcTicks}:{RingAt.UtcTicks}"
        : $"{Origin}:{ReminderId:N}:{Due.UtcTicks}:{RingAt.UtcTicks}";
}
