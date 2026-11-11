using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The due queue of task reminders (ADR 76), pure. Every live, unfinished task's reminders give one occurrence each; the ones not
/// handled whose time has come are due, Can't miss first, then the one waiting longest. A reminder missed by more than
/// <see cref="Lookback"/> (the PC was off) is not rung: it stays for the "Reminders due today" list.
/// </summary>
public static class TaskReminderQueue
{
    /// <summary>How long after its time a reminder still rings (the PC was off or asleep).</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromHours(24);

    /// <returns>Every occurrence of the tasks' reminders with its state; a task done, deleted or without a rung-able reminder gives none.</returns>
    public static List<TaskReminderEntry> Entries(IEnumerable<TaskItem> tasks, TimeZoneInfo zone)
    {
        var entries = new List<TaskReminderEntry>();
        foreach (TaskItem task in tasks)
        {
            if (!task.IsLive || task.IsDone || task.Bin == TaskBin.Done || task.Reminders is null)
            {
                continue;
            }

            foreach (TaskReminder reminder in task.Reminders)
            {
                if (TaskReminderTimes.Occurrence(task, reminder, zone) is not DateTimeOffset due)
                {
                    continue;
                }

                TaskReminderOccurrence? stored = reminder.Occurrences?.FirstOrDefault(occurrence => occurrence.Due == due);
                TaskReminderStatus status = stored?.Status ?? TaskReminderStatus.Up;
                DateTimeOffset ringAt = status == TaskReminderStatus.Snoozed && stored?.SnoozedUntil is DateTimeOffset until ? until : due;
                entries.Add(new TaskReminderEntry(task.Id, reminder.Id, reminder.Rule, due, status, ringAt, stored?.SnoozeCount ?? 0, task.CantMiss));
            }
        }

        return entries;
    }

    /// <summary>The occurrences ringing now: not handled, time reached, not older than <see cref="Lookback"/>; in ring order.</summary>
    public static List<TaskReminderEntry> Due(IEnumerable<TaskReminderEntry> entries, DateTimeOffset now)
    {
        return entries
            .Where(entry => !entry.Handled && entry.RingAt <= now && now - entry.RingAt <= Lookback)
            .OrderByDescending(entry => entry.CantMiss)
            .ThenBy(entry => entry.RingAt)
            .ThenBy(entry => entry.TaskId)
            .ThenBy(entry => entry.ReminderId)
            .ToList();
    }

    /// <returns>The earliest time after <paramref name="now"/> an unhandled occurrence rings; null when none is ahead.</returns>
    public static DateTimeOffset? NextRingAt(IEnumerable<TaskReminderEntry> entries, DateTimeOffset now)
    {
        DateTimeOffset? next = null;
        foreach (TaskReminderEntry entry in entries.Where(entry => !entry.Handled && entry.RingAt > now))
        {
            if (next is null || entry.RingAt < next)
            {
                next = entry.RingAt;
            }
        }

        return next;
    }
}
