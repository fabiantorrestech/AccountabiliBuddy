using System.Globalization;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What a ringing reminder's card shows on the PC (ADR 76, spec 03 §D, spec 05 §7), pure: the head line, the meta line, the big
/// clock of the full-screen card, the snoozed-3-times nudge, whether it shows full screen, the stack's place ("1/6") and tilted
/// cards behind, and where Dismiss stands. Times are 12-hour with AM/PM. Holds no task text itself: callers pass the label name.
/// </summary>
public static class TaskReminderCard
{
    /// <summary>Snoozed this many times, the card offers "Add to Queued".</summary>
    public const int NudgeAfterSnoozes = 3;

    /// <summary>At most this many tilted cards show behind the front one.</summary>
    public const int MaxBehind = 3;

    public const string NoLabel = "No label";

    /// <summary>"Reminder · at start time · 5 more", "Can't miss · 10 min before", "Reminder · at start time · snoozed 3 times".</summary>
    public static string Head(TaskReminderEntry entry, TimeZoneInfo zone, int more)
    {
        string head = (entry.CantMiss ? "Can't miss · " : "Reminder · ") + TaskReminderText.Rule(entry, zone);
        if (entry.SnoozeCount >= NudgeAfterSnoozes)
        {
            head += $" · snoozed {entry.SnoozeCount} times";
        }

        if (more > 0)
        {
            head += $" · {more} more";
        }

        return head;
    }

    /// <summary>"AI · 50 min · 150": the label (or "No label"), then each part the task has, joined by " · ".</summary>
    public static string Meta(string? labelName, params string?[] parts)
    {
        IEnumerable<string> shown = parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!);
        return string.Join(" · ", new[] { string.IsNullOrWhiteSpace(labelName) ? NoLabel : labelName }.Concat(shown));
    }

    /// <summary>"first rang 10:00 AM" (another day: "first rang Tue 10:00 AM"): when the occurrence was first due.</summary>
    public static string FirstRang(TaskReminderEntry entry, DateTimeOffset now, TimeZoneInfo zone)
    {
        return "first rang " + DayAndTime(entry.Due, now, zone);
    }

    /// <summary>
    /// The task's "Done by" for the full-screen card: "due 5:40 PM" today, "due tomorrow 5:40 PM", "due Thu 11/12 5:40 PM", or without
    /// a time "due today" / "due Thu 11/12"; null when it has none.
    /// </summary>
    public static string? DueText(DateOnly? dueOn, TimeOnly? dueAt, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (dueOn is not DateOnly day)
        {
            return null;
        }

        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        string dayText = day == today ? "today"
            : day == today.AddDays(1) ? "tomorrow"
            : day.ToString("ddd M/d", CultureInfo.InvariantCulture);
        if (dueAt is not TimeOnly time)
        {
            return "due " + dayText;
        }

        return day == today ? $"due {TaskSnooze.Clock(time)}" : $"due {dayText} {TaskSnooze.Clock(time)}";
    }

    /// <summary>The full-screen card's big clock: "5:30" and "PM" apart (the digits large, AM/PM small beside them).</summary>
    public static (string Digits, string Suffix) Clock(DateTimeOffset at, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        return (local.ToString("h:mm", CultureInfo.InvariantCulture), local.ToString("tt", CultureInfo.InvariantCulture));
    }

    /// <summary>A list row's time: "2:50 PM"; another day "Tue 2:50 PM".</summary>
    public static string ListTime(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        return DayAndTime(at, now, zone);
    }

    /// <summary>The italic "Add to Queued" nudge shows: snoozed 3 times or more and the task isn't already in today's plan.</summary>
    public static bool ShowsNudge(TaskReminderEntry entry, TaskBin? bin)
    {
        return entry.SnoozeCount >= NudgeAfterSnoozes && bin is TaskBin.Sort or TaskBin.Next or TaskBin.Later;
    }

    /// <summary>
    /// Shows on the dimmed full screen rather than as a toast: Can't miss always; any reminder when "How reminders show" is Always full
    /// screen, except while a session runs (a reminder then is quiet, never full screen).
    /// </summary>
    public static bool ShowsFullScreen(TaskReminderEntry entry, TaskReminderDisplay display, bool sessionRunning)
    {
        return entry.CantMiss || (display == TaskReminderDisplay.FullScreen && !sessionRunning);
    }

    /// <summary>How many tilted cards show behind the front one when <paramref name="more"/> wait behind it.</summary>
    public static int Behind(int more)
    {
        return Math.Clamp(more, 0, MaxBehind);
    }

    /// <summary>"1/6".</summary>
    public static string Position(int index, int count)
    {
        return $"{index + 1}/{count}";
    }

    /// <summary>‹ and ›: the next or previous card, round the stack.</summary>
    public static int Step(int index, int delta, int count)
    {
        return count <= 0 ? 0 : ((index + delta) % count + count) % count;
    }

    /// <summary>
    /// After the stack changed: the card shown before stays shown if it is still there; if it left (handled), the one now in its place
    /// shows (the next), or the last when it was the last.
    /// </summary>
    public static int KeepIndex(IReadOnlyList<string> oldKeys, int oldIndex, IReadOnlyList<string> newKeys)
    {
        if (newKeys.Count == 0)
        {
            return 0;
        }

        if (oldIndex >= 0 && oldIndex < oldKeys.Count)
        {
            int kept = IndexOf(newKeys, oldKeys[oldIndex]);
            if (kept >= 0)
            {
                return kept;
            }
        }

        return Math.Clamp(oldIndex, 0, newKeys.Count - 1);
    }

    /// <summary>Where Dismiss stands <paramref name="shown"/> after the card came on screen.</summary>
    public static TaskReminderDismissState Dismiss(TaskReminderRingPolicy policy, TimeSpan shown, bool cantMiss)
    {
        TimeSpan left = policy.DismissLeft(shown);
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        TimeSpan hold = policy.HoldRequired(cantMiss);
        string text = left > TimeSpan.Zero ? $"Dismiss in {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} s"
            : hold > TimeSpan.Zero ? "Hold to dismiss"
            : "Dismiss";
        return new TaskReminderDismissState(left, hold, text);
    }

    /// <summary>"Dismiss all" in the list: every reminder but Can't miss ones, which are only dismissed by their own hold.</summary>
    public static IReadOnlyList<TaskReminderEntry> DismissAll(IEnumerable<TaskReminderEntry> entries)
    {
        return entries.Where(entry => !entry.CantMiss).ToList();
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static string DayAndTime(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        string time = TaskSnooze.Clock(TimeOnly.FromDateTime(local));
        return local.Date == today.Date ? time : local.ToString("ddd", CultureInfo.InvariantCulture) + " " + time;
    }
}
