using System.Globalization;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Snooze moments (ADR 76, spec 05 §8), pure: minutes from now, the three shortcuts (Tonight 6:00 PM, Tomorrow 9:00 AM, Monday 9:00 AM
/// by default) and an absolute date and time. A moment that is not in the future gives null. Times are 12-hour with AM/PM.
/// </summary>
public static class TaskSnooze
{
    /// <summary>The snooze lengths of the menu.</summary>
    public static readonly IReadOnlyList<int> Minutes = [5, 10, 30];

    public static IReadOnlyList<TaskSnoozeShortcut> DefaultShortcuts { get; } =
    [
        new(SnoozeDay.Today, new TimeOnly(18, 0)),
        new(SnoozeDay.Tomorrow, new TimeOnly(9, 0)),
        new(SnoozeDay.NextMonday, new TimeOnly(9, 0)),
    ];

    public static DateTimeOffset FromNow(DateTimeOffset now, int minutes)
    {
        return now + TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    /// <returns>The moment the shortcut means from <paramref name="now"/>; null when it already passed (Tonight 6:00 PM at 7 PM).</returns>
    public static DateTimeOffset? Resolve(TaskSnoozeShortcut shortcut, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        DateOnly day = shortcut.Day switch
        {
            SnoozeDay.Today => today,
            SnoozeDay.Tomorrow => today.AddDays(1),
            SnoozeDay.NextMonday => NextWeekday(today, DayOfWeek.Monday, includeToday: false),
            _ => NextWeekday(today, shortcut.Weekday, includeToday: true),
        };
        DateTimeOffset at = TaskReminderTimes.Local(day, shortcut.Time, zone);
        if (at <= now && shortcut.Day == SnoozeDay.Weekday)
        {
            at = TaskReminderTimes.Local(day.AddDays(7), shortcut.Time, zone);
        }

        return at > now ? at : null;
    }

    /// <returns>The moment of a picked date and time; null when it has passed.</returns>
    public static DateTimeOffset? Absolute(DateOnly date, TimeOnly time, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTimeOffset at = TaskReminderTimes.Local(date, time, zone);
        return at > now ? at : null;
    }

    /// <summary>A shortcut's label: "Tonight 6:00 PM", "Tomorrow 9:00 AM", "Monday 9:00 AM".</summary>
    public static string Label(TaskSnoozeShortcut shortcut)
    {
        string time = Clock(shortcut.Time);
        string day = shortcut.Day switch
        {
            SnoozeDay.Today => shortcut.Time >= new TimeOnly(17, 0) ? "Tonight" : "Today",
            SnoozeDay.Tomorrow => "Tomorrow",
            SnoozeDay.NextMonday => "Monday",
            _ => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(shortcut.Weekday),
        };
        return $"{day} {time}";
    }

    /// <summary>The picker's summary: "Thursday, Nov 12th · 9:00 AM", with the year only when it isn't this year.</summary>
    public static string Summary(DateTimeOffset moment, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(moment, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        CultureInfo culture = CultureInfo.InvariantCulture;
        string year = local.Year == today.Year ? string.Empty : $", {local.Year}";
        return $"{culture.DateTimeFormat.GetDayName(local.DayOfWeek)}, {culture.DateTimeFormat.GetAbbreviatedMonthName(local.Month)} {Ordinal(local.Day)}{year} · {Clock(TimeOnly.FromDateTime(local))}";
    }

    /// <summary>12-hour time with AM/PM, e.g. 4:05 PM.</summary>
    public static string Clock(TimeOnly time)
    {
        return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string Ordinal(int day)
    {
        string suffix = day % 100 is >= 11 and <= 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek weekday, bool includeToday)
    {
        int ahead = ((int)weekday - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(ahead == 0 && !includeToday ? 7 : ahead);
    }
}
