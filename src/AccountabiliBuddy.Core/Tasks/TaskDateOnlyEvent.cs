namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// "Tasks with a date but no time" (ADR 78, owner 2026-11-11): what event a task gets in Google Calendar when its due date has no time.
/// </summary>
public enum TaskDateOnlyEvent
{
    /// <summary>A timed event at 9:00 AM (the time a whole-day due date's reminders count from), so same-day reminders go on it (the default).</summary>
    TimedAtNine,

    /// <summary>An all-day event; Google can't carry its same-day reminders (they are left off and counted).</summary>
    AllDay,
}
