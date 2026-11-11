namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The calendar event one task becomes (ADR 78): its title and times only, never notes, steps, tags, labels or reasons.
/// Either an all-day event (<see cref="Day"/> set) or a timed one (<see cref="Start"/> and <see cref="End"/> set).
/// </summary>
/// <param name="Title">The task's title (private text: never logged).</param>
/// <param name="Day">The day of an all-day event (a due date without a time); null for a timed event.</param>
/// <param name="Start">The start of a timed event; null for an all-day one.</param>
/// <param name="End">The end of a timed event; null for an all-day one.</param>
/// <param name="ReminderMinutes">The event's reminder overrides, minutes before its start, ascending, at most <see cref="TaskCalendarMapping.MaxOverrides"/>.</param>
/// <param name="IsAnchor">A task with reminders but no due date: the event only carries the reminders.</param>
/// <param name="Dropped">How many of the task's reminders the event can't carry (before an all-day event's start, or too far ahead).</param>
public sealed record TaskCalendarEvent(
    string Title,
    DateOnly? Day,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    IReadOnlyList<int> ReminderMinutes,
    bool IsAnchor,
    int Dropped)
{
    public bool IsAllDay => Day is not null;
}
