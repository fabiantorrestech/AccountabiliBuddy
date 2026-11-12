namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Where a ringing reminder comes from (round 2 R5.5): a task of the shared task list (ADR 76), or a reminder source that rings through the
/// same queue, ringer, snooze and session rules without being a task, such as a goal reminder (brief §5.9). Never stored or synced.
/// </summary>
public enum TaskReminderOrigin
{
    /// <summary>A task's reminder: <see cref="TaskReminderEntry.TaskId"/> is the task.</summary>
    Task,

    /// <summary>A goal reminder or nudge: <see cref="TaskReminderEntry.TaskId"/> is the goal (never a task), and nothing is written to a task.</summary>
    Goal,
}
