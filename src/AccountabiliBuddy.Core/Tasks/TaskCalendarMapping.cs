using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Turns a task into its one calendar event (ADR 78). Only the title and times go into it.
/// <list type="bullet">
/// <item>A due date and time: a <see cref="TimedLength"/> event at the due time; each reminder is an override, minutes before it.</item>
/// <item>A due date only: an all-day event that day. Google counts an all-day event's reminders back from its midnight start, so a
/// reminder on the day itself (e.g. "At start time", 9:00 AM) can't be carried and is <see cref="TaskCalendarEvent.Dropped"/>;
/// "1 day before at 9:00 AM" is 900 minutes.</item>
/// <item>Reminders but no due date (only "at" reminders can ring then): the anchor, a <see cref="TimedLength"/> event at the task's
/// last reminder, with every reminder as minutes before it (the last one at 0), so each still rings at its own time.</item>
/// </list>
/// Overrides keep Google's limits: 0 to <see cref="MaxMinutes"/> minutes, at most <see cref="MaxOverrides"/>.
/// </summary>
public static class TaskCalendarMapping
{
    /// <summary>The key of the app's own "AB Reminders" calendar in <see cref="TaskCalendarWanted.CalendarKey"/>.</summary>
    public const string AppCalendarKey = "app";

    /// <summary>Google's most reminder overrides on one event (why a task holds at most five reminders).</summary>
    public const int MaxOverrides = TaskReminder.MaxPerTask;

    /// <summary>Google's furthest reminder: four weeks before the event.</summary>
    public const int MaxMinutes = 40320;

    /// <summary>How long a timed event (a due time or an anchor) lasts.</summary>
    public static readonly TimeSpan TimedLength = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The task's Google event id: "ab" and its id's 32 hex digits, which Google's event-id alphabet (base32hex, a to v and 0 to 9)
    /// allows. The same on every device and after a reinstall, so a task never gets a second event in a calendar.
    /// </summary>
    public static string EventId(Guid taskId)
    {
        return "ab" + taskId.ToString("N");
    }

    /// <summary>
    /// Whether the task is exported at all: live, open, not in Done, its "Add to Google Calendar" switch on, and kept on this device
    /// (<paramref name="self"/>; a task kept on other devices only is left to them).
    /// </summary>
    public static bool Exports(TaskItem task, Guid? self)
    {
        return task.IsLive
               && !task.IsDone
               && task.Bin != TaskBin.Done
               && task.Calendar
               && (task.Devices is null || (self is Guid device && task.Devices.Contains(device)));
    }

    /// <returns>The task's event; null when it has neither a due date nor a reminder that can ring.</returns>
    public static TaskCalendarEvent? EventFor(TaskItem task, TimeZoneInfo zone)
    {
        string title = task.Title.Trim() is { Length: > 0 } trimmed ? trimmed : "Task";
        List<DateTimeOffset> rings = (task.Reminders ?? [])
            .Select(reminder => TaskReminderTimes.Occurrence(task, reminder, zone))
            .OfType<DateTimeOffset>()
            .Distinct()
            .ToList();

        if (task.DueOn is DateOnly dueOn)
        {
            if (task.DueAt is TimeOnly dueAt)
            {
                DateTimeOffset start = TaskReminderTimes.Local(dueOn, dueAt, zone);
                (IReadOnlyList<int> minutes, int dropped) = Overrides(start, rings);
                return new TaskCalendarEvent(title, null, start, start + TimedLength, minutes, false, dropped);
            }

            DateTimeOffset midnight = TaskReminderTimes.Local(dueOn, TimeOnly.MinValue, zone);
            (IReadOnlyList<int> dayMinutes, int dayDropped) = Overrides(midnight, rings);
            return new TaskCalendarEvent(title, dueOn, null, null, dayMinutes, false, dayDropped);
        }

        if (rings.Count == 0)
        {
            return null;
        }

        DateTimeOffset anchor = rings.Max();
        (IReadOnlyList<int> anchorMinutes, int anchorDropped) = Overrides(anchor, rings);
        return new TaskCalendarEvent(title, null, anchor, anchor + TimedLength, anchorMinutes, true, anchorDropped);
    }

    /// <summary>
    /// The events the tasks should have now, by task id. With a picked calendar (the experimental option, its consent given), tasks
    /// with a due date go there and the rest to the app's own calendar; otherwise every one goes to the app's own. Still one event
    /// per task.
    /// </summary>
    public static IReadOnlyDictionary<Guid, TaskCalendarWanted> Wanted(
        IEnumerable<TaskItem> tasks, TimeZoneInfo zone, Guid? self, string? pickedCalendarId)
    {
        var wanted = new Dictionary<Guid, TaskCalendarWanted>();
        foreach (TaskItem task in tasks.Where(task => Exports(task, self)))
        {
            if (EventFor(task, zone) is not { } calendarEvent)
            {
                continue;
            }

            string key = pickedCalendarId is { Length: > 0 } picked && task.DueOn is not null ? picked : AppCalendarKey;
            wanted[task.Id] = new TaskCalendarWanted(key, calendarEvent, Signature(calendarEvent));
        }

        return wanted;
    }

    /// <summary>A hash of everything the event carries (16 hex digits of SHA-256): equal events, equal signatures. Holds no text.</summary>
    public static string Signature(TaskCalendarEvent calendarEvent)
    {
        string text = string.Join(
            "\n",
            calendarEvent.Title,
            calendarEvent.Day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
            calendarEvent.Start?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "-",
            calendarEvent.End?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "-",
            string.Join(",", calendarEvent.ReminderMinutes.Select(minutes => minutes.ToString(CultureInfo.InvariantCulture))));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8));
    }

    private static (IReadOnlyList<int> Minutes, int Dropped) Overrides(DateTimeOffset start, IReadOnlyList<DateTimeOffset> rings)
    {
        List<int> minutes = rings
            .Select(ring => (int)Math.Round((start - ring).TotalMinutes, MidpointRounding.AwayFromZero))
            .Where(before => before is >= 0 and <= MaxMinutes)
            .Distinct()
            .Order()
            .Take(MaxOverrides)
            .ToList();
        return (minutes, rings.Count - minutes.Count);
    }
}
