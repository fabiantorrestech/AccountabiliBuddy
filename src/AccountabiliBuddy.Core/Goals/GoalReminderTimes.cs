using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// When a goal reminder rings (brief §5.9), pure: on each day its <see cref="GoalReminder.Days"/> name, at its local
/// <see cref="GoalReminder.Time"/> (a time that doesn't exist or happens twice on a clock change resolves as task reminders do,
/// <see cref="TaskReminderTimes.Local"/>). A reminder never rings for a moment before it was set or last changed
/// (<see cref="GoalReminder.EditedAt"/>), so adding "every day at 8:00 PM" at 9:00 PM waits for tomorrow. One that doesn't repeat
/// rings once: on its date ("Today", "On a date"), or on its first weekday after it was set.
/// </summary>
public static class GoalReminderTimes
{
    /// <summary>The morning the "Nudge when the week falls behind" rings (brief §5.9: "the morning after").</summary>
    public static readonly TimeOnly NudgeTime = new(9, 0);

    /// <returns>The moment the reminder rings on <paramref name="day"/>; null when it doesn't ring that day.</returns>
    public static DateTimeOffset? On(GoalReminder reminder, DateOnly day, TimeZoneInfo zone)
    {
        if (!reminder.IsLive || !RingsOn(reminder, day, zone))
        {
            return null;
        }

        DateTimeOffset at = TaskReminderTimes.Local(day, reminder.Time, zone);
        return at < reminder.EditedAt ? null : at;
    }

    /// <summary>The nudge's moment on <paramref name="day"/> (9:00 AM local).</summary>
    public static DateTimeOffset NudgeOn(DateOnly day, TimeZoneInfo zone)
    {
        return TaskReminderTimes.Local(day, NudgeTime, zone);
    }

    private static bool RingsOn(GoalReminder reminder, DateOnly day, TimeZoneInfo zone)
    {
        switch (reminder.Days)
        {
            case GoalReminderDays.EveryDay:
                return true;
            case GoalReminderDays.Today:
            case GoalReminderDays.Date:
                return reminder.Date == day;
            case GoalReminderDays.Weekdays:
                if (reminder.Weekdays is not { Count: > 0 } weekdays || !weekdays.Contains((GoalWeekday)day.DayOfWeek))
                {
                    return false;
                }

                if (reminder.Repeat)
                {
                    return true;
                }

                // Once: the first of its weekdays on or after the day it was set (a time already gone that day moves to the next one).
                DateOnly from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reminder.EditedAt, zone).DateTime);
                for (DateOnly candidate = from; candidate <= from.AddDays(7); candidate = candidate.AddDays(1))
                {
                    if (weekdays.Contains((GoalWeekday)candidate.DayOfWeek) && TaskReminderTimes.Local(candidate, reminder.Time, zone) >= reminder.EditedAt)
                    {
                        return candidate == day;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}
