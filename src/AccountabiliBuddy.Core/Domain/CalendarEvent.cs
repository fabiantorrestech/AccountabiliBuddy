using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// One calendar event, from the in-app calendar or a connected calendar, in local wall-clock time.
/// Events never start or stop blocks by themselves; a classified one (ADR 58) shapes the day plan's guarded windows.
/// </summary>
/// <param name="Id">Unique within its calendar (a repeating event's occurrences get distinct ids).</param>
/// <param name="LocalStart">Start; midnight for an all-day event.</param>
/// <param name="LocalEnd">End (exclusive); midnight after the last day for an all-day event.</param>
/// <param name="SeriesId">
/// What stays the same across a repeating event's occurrences (iCal UID, Google's recurring event id) and, for iCal, across a
/// single event being moved; null for events of the in-app calendar and Google's single events. Leaves the device only in the sealed sync file, on an event that resolves to a day plan category (ADR 58).
/// </param>
/// <param name="OccurrenceKey">
/// Which occurrence of the series this is, by its original time (iCal RECURRENCE-ID or the occurrence's start, Google's
/// original start time), so moving one occurrence doesn't change it; null when the event doesn't repeat.
/// </param>
public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTime LocalStart,
    DateTime LocalEnd,
    bool IsAllDay,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SeriesId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OccurrenceKey = null)
{
    /// <summary>
    /// What a "this event" classification is stored under: the occurrence's series and original time when the source gives
    /// them (so it follows the event when it moves), else the event's id.
    /// </summary>
    [JsonIgnore]
    public string ClassificationKey => SeriesId is null ? Id : $"{SeriesId}#{OccurrenceKey}";

    /// <summary>True when any part of the event falls on <paramref name="date"/>.</summary>
    public bool OccursOn(DateOnly date)
    {
        DateTime dayStart = date.ToDateTime(TimeOnly.MinValue);
        DateTime dayEnd = dayStart.AddDays(1);
        DateTime end = LocalEnd > LocalStart ? LocalEnd : LocalStart.AddMinutes(1);
        return LocalStart < dayEnd && end > dayStart;
    }
}
