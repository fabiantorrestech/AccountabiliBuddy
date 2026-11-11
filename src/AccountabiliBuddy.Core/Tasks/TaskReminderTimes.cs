using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// When a task's reminder rings (ADR 76), worked out from its rule and the task's "Done by" date and time. Local times are turned into
/// instants with the zone's rules: a time that does not exist (spring forward) moves to the first moment that does, and one that
/// happens twice (fall back) means the first time. "N before" counts real minutes, so it holds across a clock change.
/// </summary>
public static class TaskReminderTimes
{
    /// <summary>The time of day that stands for a due date with no time ("Done by" a whole day), and the "1 day before" reminder's time.</summary>
    public static readonly TimeOnly NineAm = new(9, 0);

    /// <returns>The moment the reminder rings; null when it can't ring (a relative rule without a due date, an unknown rule from a newer app, an absolute one with no time).</returns>
    public static DateTimeOffset? Occurrence(TaskItem task, TaskReminder reminder, TimeZoneInfo zone)
    {
        if (reminder.Rule == TaskReminder.Absolute)
        {
            return reminder.At;
        }

        if (task.DueOn is not DateOnly dueOn)
        {
            return null;
        }

        DateTimeOffset due = Local(dueOn, task.DueAt ?? NineAm, zone);
        return reminder.Rule switch
        {
            TaskReminder.AtStart => due,
            TaskReminder.TenMinutesBefore => due - TimeSpan.FromMinutes(10),
            TaskReminder.OneHourBefore => due - TimeSpan.FromHours(1),
            TaskReminder.DayBeforeAtNine => Local(dueOn.AddDays(-1), NineAm, zone),
            _ => null,
        };
    }

    /// <summary>A local date and time as an instant in <paramref name="zone"/>.</summary>
    public static DateTimeOffset Local(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
