using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>The words that name a reminder rule (ADR 76), always with 12-hour times.</summary>
public static class TaskReminderText
{
    /// <summary>E.g. "at start time", "10 min before", "1 hour before", "1 day before at 9:00 AM", "at 5:30 PM" (absolute, in <paramref name="zone"/>).</summary>
    public static string Rule(TaskReminderEntry entry, TimeZoneInfo zone)
    {
        return entry.Rule switch
        {
            TaskReminder.AtStart => "at start time",
            TaskReminder.TenMinutesBefore => "10 min before",
            TaskReminder.OneHourBefore => "1 hour before",
            TaskReminder.DayBeforeAtNine => "1 day before at " + TaskSnooze.Clock(TaskReminderTimes.NineAm),
            _ => "at " + TaskSnooze.Clock(TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.Due, zone).DateTime)),
        };
    }
}
