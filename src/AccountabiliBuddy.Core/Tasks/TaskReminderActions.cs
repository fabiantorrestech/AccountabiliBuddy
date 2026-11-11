using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What the user does to one occurrence of a task reminder (ADR 76), pure: snooze, dismiss, start. Each writes the occurrence's state
/// into the task's reminders as an edit by this device (<see cref="TaskEdits"/>), so it syncs and the other devices stop ringing it.
/// The occurrence is found by its due time; a reminder keeps only its current occurrence's state (an earlier due time's is dropped).
/// A task or reminder that is gone, or an occurrence already handled, changes nothing.
/// </summary>
public static class TaskReminderActions
{
    /// <summary>Rings it again at <paramref name="until"/>; counts the snooze (3 or more shows the "Put it in Queued?" nudge).</summary>
    public static TaskActionResult Snooze(TaskLists lists, TaskReminderEntry entry, DateTimeOffset until, DateTimeOffset now, Guid self)
    {
        return Write(lists, entry, now, self, existing => existing is { Status: TaskReminderStatus.Dismissed or TaskReminderStatus.Started }
            ? existing
            : new TaskReminderOccurrence(entry.Due, TaskReminderStatus.Snoozed, until, (existing?.SnoozeCount ?? 0) + 1));
    }

    public static TaskActionResult Dismiss(TaskLists lists, TaskReminderEntry entry, DateTimeOffset now, Guid self)
    {
        return Write(lists, entry, now, self, existing => existing is { Status: TaskReminderStatus.Started }
            ? existing
            : new TaskReminderOccurrence(entry.Due, TaskReminderStatus.Dismissed, null, existing?.SnoozeCount ?? 0));
    }

    /// <summary>"Start a session" / "Work on it now": the occurrence is handled. Moving the task into This session is the session's own path.</summary>
    public static TaskActionResult Start(TaskLists lists, TaskReminderEntry entry, DateTimeOffset now, Guid self)
    {
        return Write(lists, entry, now, self, existing => new TaskReminderOccurrence(entry.Due, TaskReminderStatus.Started, null, existing?.SnoozeCount ?? 0));
    }

    private static TaskActionResult Write(
        TaskLists lists, TaskReminderEntry entry, DateTimeOffset now, Guid self, Func<TaskReminderOccurrence?, TaskReminderOccurrence> change)
    {
        int index = lists.Tasks.ToList().FindIndex(item => item.Id == entry.TaskId && item.Task.IsLive);
        if (index < 0 || lists.Tasks[index].Task.Reminders is not { } reminders)
        {
            return TaskActionResult.None(lists);
        }

        int reminderIndex = reminders.ToList().FindIndex(reminder => reminder.Id == entry.ReminderId);
        if (reminderIndex < 0)
        {
            return TaskActionResult.None(lists);
        }

        TaskReminder reminder = reminders[reminderIndex];
        TaskReminderOccurrence? existing = reminder.Occurrences?.FirstOrDefault(occurrence => occurrence.Due == entry.Due);
        TaskReminderOccurrence changed = change(existing);
        if (changed == existing)
        {
            return TaskActionResult.None(lists);
        }

        List<TaskReminder> updated = reminders.ToList();
        updated[reminderIndex] = reminder with { Occurrences = [changed] };
        SyncTaskItem item = lists.Tasks[index];
        SyncTaskItem edited = TaskEdits.Edit(item, TaskField.Reminders, item.Task with { Reminders = updated }, now, self);
        if (ReferenceEquals(edited, item))
        {
            return TaskActionResult.None(lists);
        }

        List<SyncTaskItem> tasks = lists.Tasks.ToList();
        tasks[index] = edited;
        return new TaskActionResult(lists with { Tasks = tasks }, string.Empty, [entry.TaskId]);
    }
}
