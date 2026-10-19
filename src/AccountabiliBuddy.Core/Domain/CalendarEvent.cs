namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One calendar event, from the in-app calendar or a connected calendar, in local wall-clock time.
/// Events are for planning only: they never start or stop blocks by themselves.
/// </summary>
/// <param name="Id">Unique within its calendar (a repeating event's occurrences get distinct ids).</param>
/// <param name="LocalStart">Start; midnight for an all-day event.</param>
/// <param name="LocalEnd">End (exclusive); midnight after the last day for an all-day event.</param>
public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime LocalStart,
    DateTime LocalEnd,
    bool IsAllDay)
{
    /// <summary>True when any part of the event falls on <paramref name="date"/>.</summary>
    public bool OccursOn(DateOnly date)
    {
        DateTime dayStart = date.ToDateTime(TimeOnly.MinValue);
        DateTime dayEnd = dayStart.AddDays(1);
        DateTime end = LocalEnd > LocalStart ? LocalEnd : LocalStart.AddMinutes(1);
        return LocalStart < dayEnd && end > dayStart;
    }
}
